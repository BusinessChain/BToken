using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    class ChainInserter
    {
      Headerchain Headerchain;
      ChainProbe Probe;
      public double AccumulatedDifficulty;


      public ChainInserter(Headerchain headerchain)
      {
        Headerchain = headerchain;
        Probe = new ChainProbe(headerchain.MainChain);
      }

      public void Initialize()
      {
        Probe.Initialize();
        AccumulatedDifficulty = Probe.Chain.AccumulatedDifficulty;
      }
      public void Push()
      {
        Probe.Push();
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Probe.Header.NBits);
      }

      List<Header> ValidateChain(Header headerRoot)
      {
        var headersValidated = new List<Header>();
        Header header = headerRoot;

        while (true)
        {
          uint medianTimePast = GetMedianTimePast(header.HeaderPrevious);
          if (header.UnixTimeSeconds < medianTimePast)
          {
            throw new ChainException(
              string.Format("header {0} with unix time {1} older than median time past {2}",
              header.HeaderHash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)));
          }

          ValidateCheckpoint(header.HeaderHash, Probe.GetHeight() + headersValidated.Count + 1);

          uint targetBits = TargetManager.GetNextTargetBits(
              header.HeaderPrevious,
              (uint)(Probe.GetHeight() + headersValidated.Count + 1));

          if (header.NBits != targetBits)
          {
            throw new ChainException(
              string.Format("In header {0} nBits {1} not equal to target nBits {2}",
              header.HeaderHash.ToHexString(),
              header.NBits,
              targetBits));
          }

          headersValidated.Add(header);

          if (header.HeadersNext.Any())
          {
            header = header.HeadersNext[0];
          }
          else
          {
            return headersValidated;
          }
        }
      }

      public Chain InsertHeaderRoot(Header headerRoot)
      {
        FindPreviousHeader(headerRoot);

        if (Probe.Header.HeadersNext
          .Any(h => h.HeaderHash.IsEqual(headerRoot.HeaderHash)))
        {
          throw new ChainException(
            string.Format("duplicate header {0} \n attempting to connect to header {1}",
            headerRoot.HeaderHash.ToHexString(),
            headerRoot.HashPrevious.ToHexString()));
        }

        headerRoot.HeaderPrevious = Probe.Header;

        List<Header> headersValidated = ValidateChain(headerRoot);

        Probe.Header.HeadersNext.Add(headerRoot);

        headersValidated.ForEach(h => Headerchain.UpdateHeaderIndex(h));

        if (Probe.IsTip())
        {
          Probe.Chain.HeaderTip = headersValidated.Last();
          Probe.Chain.Height += headersValidated.Count;
          Probe.Chain.AccumulatedDifficulty += headersValidated
            .Sum(h => TargetManager.GetDifficulty(h.NBits));

          if (Probe.Chain == Headerchain.MainChain)
          {
            Headerchain.Locator.Update();
            return null;
          }

          return Probe.Chain;
        }
        else
        {
          Chain chainFork = new Chain(
            headerRoot: headerRoot,
            height: Probe.GetHeight() + headersValidated.Count,
            accumulatedDifficulty: AccumulatedDifficulty + headersValidated
            .Sum(h => TargetManager.GetDifficulty(h.NBits)));

          Headerchain.SecondaryChains.Add(chainFork);

          return chainFork;
        }
      }
      void TryValidateChain(Header headerRoot, int height)
      {
        Header header = headerRoot;
        double accumulatedDifficulty = 0;

        for (int i = 0; i < height; i += 1)
        {
          uint medianTimePast = GetMedianTimePast(header.HeaderPrevious);
          if (header.UnixTimeSeconds < medianTimePast)
          {
            throw new ChainException(
              string.Format("header {0} with unix time {1} older than median time past {2}",
              header.HeaderHash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)));
          }

          ValidateCheckpoint(header.HeaderHash, Probe.GetHeight() + i + 1);

          uint targetBits = TargetManager.GetNextTargetBits(
              header.HeaderPrevious,
              (uint)(Probe.GetHeight() + i + 1));

          if (header.NBits != targetBits)
          {
            throw new ChainException(
              string.Format("In header {0} nBits {1} not equal to target nBits {2}",
              header.HeaderHash.ToHexString(),
              header.NBits,
              targetBits));
          }

          header = header.HeadersNext[0];
          accumulatedDifficulty += TargetManager.GetDifficulty(header.NBits);
        }
      }
      void FindPreviousHeader(Header header)
      {
        Probe.Chain = Headerchain.MainChain;

        if (Probe.GoTo(
          header.HashPrevious, 
          Headerchain.MainChain.HeaderRoot))
        { return; }

        foreach (Chain chain in Headerchain.SecondaryChains)
        {
          Probe.Chain = chain;
          if (Probe.GoTo(
            header.HashPrevious, 
            chain.HeaderRoot))
          { return; }
        }

        throw new ChainException(
          string.Format("previous header {0}\n of header {1} not found in headerchain",
          header.HashPrevious.ToHexString(),
          header.HeaderHash.ToHexString()));
      }
      void ValidateCheckpoint(byte[] headerHash, int headerHeight)
      {
        int hightHighestCheckpoint = Headerchain.Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= Headerchain.MainChain.Height &&
          headerHeight <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format("Attempt to insert header {0} at hight {1} prior to checkpoint hight {2}",
            headerHash.ToHexString(),
            headerHeight,
            hightHighestCheckpoint));
        }

        HeaderLocation checkpoint = Headerchain.Checkpoints.Find(c => c.Height == headerHeight);
        if (checkpoint != null && !checkpoint.Hash.IsEqual(headerHash))
        {
          throw new ChainException(
            string.Format("Header {0} at hight {1} not equal to checkpoint hash {2}",
            headerHash.ToHexString(),
            headerHeight,
            checkpoint.Hash.ToHexString()));
        }

      }
      uint GetMedianTimePast(Header header)
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(header.UnixTimeSeconds);

          if (header.HeaderPrevious == null)
          { break; }

          header = header.HeaderPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }

    }
  }
}
