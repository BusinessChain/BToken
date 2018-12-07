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
    class ChainInserter : ChainProbe, IDisposable
    {
      Headerchain Headerchain;
      public double AccumulatedDifficulty;

      List<ChainLocation> Checkpoints;
      uint HighestCheckpointHight;

      readonly object IsDispatchedLOCK = new object();
      bool IsDispatched = false;

           
      public ChainInserter(Chain chain, Headerchain headerchain, List<ChainLocation> checkpoints) : base(chain)
      {
        Headerchain = headerchain;
        AccumulatedDifficulty = Chain.AccumulatedDifficulty;
        
        Checkpoints = checkpoints;
        HighestCheckpointHight = checkpoints.Max(x => x.Height);

        Headerchain.SignalInserterAvailable.Post(true);
      }

      public void Initialize()
      {
        base.Initialize();
        AccumulatedDifficulty = Chain.AccumulatedDifficulty;
      }
      public override void Push()
      {
        base.Push();
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Header.NetworkHeader.NBits);
      }

      void GoToPreviousHeader(NetworkHeader header)
      {
        Chain = Headerchain.MainChain;

        if (GoTo(header.HashPrevious))
        {
          return;
        }

        foreach (Chain chain in Headerchain.SecondaryChains)
        {
          Chain = chain;

          if (GoTo(header.HashPrevious))
          {
            return;
          }
        }

        throw new ChainException(BlockCode.ORPHAN);

      }

      public Chain InsertHeader(NetworkHeader networkHeader, UInt256 headerHash)
      {
        GoToPreviousHeader(networkHeader);

        ValidateHeader(networkHeader, headerHash);

        var chainHeader = new ChainHeader(networkHeader, Header);

        Header.HeadersNext.Add(chainHeader);
        
        if (IsTip())
        {
          Chain.ExtendChain(chainHeader, headerHash);

          if (Chain == Headerchain.MainChain)
          {
            Headerchain.Locator.Update();
            return null;
          }

          return Chain;
        }
        else
        {
          Chain chainForked = ForkChain(headerHash);
          Headerchain.SecondaryChains.Add(chainForked);
          return ForkChain(headerHash);
        }
      }
      void ValidateHeader(NetworkHeader header, UInt256 headerHash)
      {
        ValidateTimeStamp(header.UnixTimeSeconds);
        ValidateCheckpoint(headerHash);
        ValidateUniqueness(headerHash);
        ValidateProofOfWork(header.NBits, headerHash);
      }
      void ValidateCheckpoint(UInt256 headerHash)
      {
        uint nextHeaderHeight = GetHeight() + 1;

        bool mainChainLongerThanHighestCheckpoint = HighestCheckpointHight <= Headerchain.MainChain.Height;
        bool nextHeightBelowHighestCheckpoint = nextHeaderHeight <= HighestCheckpointHight;
        if (mainChainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
        {
          throw new ChainException(BlockCode.INVALID);
        }

        if (!ValidateBlockLocation(nextHeaderHeight, headerHash))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      bool ValidateBlockLocation(uint height, UInt256 hash)
      {
        ChainLocation checkpoint = Checkpoints.Find(c => c.Height == height);
        if (checkpoint != null)
        {
          return checkpoint.Hash.IsEqual(hash);
        }

        return true;
      }
      void ValidateProofOfWork(uint nBits, UInt256 headerHash)
      {
        uint nextHeight = GetHeight() + 1;
        if (nBits != TargetManager.GetNextTargetBits(Header, nextHeight))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateTimeStamp(uint unixTimeSeconds)
      {
        if (unixTimeSeconds <= GetMedianTimePast(Header))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateUniqueness(UInt256 hash)
      {
        if (Header.HeadersNext.Any(h => GetHeaderHash(h).IsEqual(hash)))
        {
          throw new ChainException(BlockCode.DUPLICATE);
        }
      }
      uint GetMedianTimePast(ChainHeader header)
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(header.NetworkHeader.UnixTimeSeconds);

          if (header.HeaderPrevious == null)
          { break; }

          header = header.HeaderPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }

      ChainHeader ConnectHeader(NetworkHeader header)
      {
        var chainHeader = new ChainHeader(header, Header);
        Header.HeadersNext.Add(chainHeader);
        return chainHeader;
      }
      public Chain ForkChain(UInt256 headerHash)
      {
        ChainHeader header = Header.HeadersNext.Last();
        uint height = GetHeight() + 1;

        return new Chain(
          headerRoot: Header,
          height: height,
          accumulatedDifficultyPrevious: AccumulatedDifficulty);
      }


      public bool TryDispatch()
      {
        lock (IsDispatchedLOCK)
        {
          if (IsDispatched)
          {
            return false;
          }

          IsDispatched = true;
          return true;
        }
      }
      public void Dispose()
      {
        lock (IsDispatchedLOCK)
        {
          IsDispatched = false;
          Headerchain.SignalInserterAvailable.Post(true);
        }
      }
    }
  }
}
