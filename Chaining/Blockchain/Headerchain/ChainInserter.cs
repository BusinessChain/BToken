using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public partial class Headerchain
    {
      class ChainInserter : IDisposable
      {
        Headerchain Headerchain;
        ChainProbe Probe;
        public double AccumulatedDifficulty;

        readonly object IsDispatchedLOCK = new object();
        bool IsDispatched = false;


        public ChainInserter(Headerchain headerchain)
        {
          Headerchain = headerchain;
          Probe = new ChainProbe(headerchain.MainChain);
          Headerchain.SignalInserterAvailable.Post(true);
        }

        public void Initialize()
        {
          Probe.Initialize();
          AccumulatedDifficulty = Probe.Chain.AccumulatedDifficulty;
        }
        public void Push()
        {
          Probe.Push();
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Probe.Header.NetworkHeader.NBits);
        }

        public Chain InsertHeader(NetworkHeader networkHeader, byte[] headerHash)
        {
          FindPreviousHeader(networkHeader);

          ValidateHeader(networkHeader, headerHash);

          var chainHeader = new ChainHeader(
            networkHeader,
            headerPrevious: Probe.Header);

          if (Probe.Header.HeadersNext == null)
          {
            Probe.Header.HeadersNext = new ChainHeader[1] { chainHeader };
          }
          else
          {
            var headersNextNew = new ChainHeader[Probe.Header.HeadersNext.Length + 1];
            Probe.Header.HeadersNext.CopyTo(headersNextNew, 0);
            headersNextNew[Probe.Header.HeadersNext.Length] = chainHeader;

            Probe.Header.HeadersNext = headersNextNew;
          }

          Headerchain.UpdateHeaderIndex(chainHeader, headerHash);

          if (Probe.IsTip())
          {
            Probe.Chain.ExtendChain(chainHeader, headerHash);

            if (Probe.Chain == Headerchain.MainChain)
            {
              Headerchain.Locator.Update();
              return null;
            }

            return Probe.Chain;
          }
          else
          {
            Chain chainForked = new Chain(
              headerRoot: chainHeader,
              height: Probe.GetHeight() + 1,
              accumulatedDifficultyPrevious: AccumulatedDifficulty);

            Headerchain.SecondaryChains.Add(chainForked);

            return chainForked;
          }
        }
        void FindPreviousHeader(NetworkHeader header)
        {
          Probe.Chain = Headerchain.MainChain;
          if (Probe.GoTo(header.HashPrevious, Headerchain.MainChain.HeaderRoot))
          { return; }

          foreach (Chain chain in Headerchain.SecondaryChains)
          {
            Probe.Chain = chain;
            if (Probe.GoTo(header.HashPrevious, chain.HeaderRoot)) { return; }
          }

          throw new ChainException(ChainCode.ORPHAN);
        }
        void ValidateHeader(NetworkHeader header, byte[] headerHash)
        {
          ValidateTimeStamp(header.UnixTimeSeconds);
          ValidateCheckpoint(headerHash);
          ValidateUniqueness(headerHash);
          ValidateProofOfWork(header.NBits);
        }
        void ValidateCheckpoint(byte[] headerHash)
        {
          int nextHeaderHeight = Probe.GetHeight() + 1;

          int highestCheckpointHight = Headerchain.Checkpoints.Max(x => x.Height);
          bool mainChainLongerThanHighestCheckpoint = highestCheckpointHight <= Headerchain.MainChain.Height;
          bool nextHeightBelowHighestCheckpoint = nextHeaderHeight <= highestCheckpointHight;
          if (mainChainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
          {
            throw new ChainException(ChainCode.INVALID);
          }

          if (!ValidateBlockLocation(nextHeaderHeight, headerHash))
          {
            throw new ChainException(ChainCode.INVALID);
          }
        }
        bool ValidateBlockLocation(int height, byte[] hash)
        {
          HeaderLocation checkpoint = Headerchain.Checkpoints.Find(c => c.Height == height);
          if (checkpoint != null)
          {
            return checkpoint.Hash.IsEqual(hash);
          }

          return true;
        }
        void ValidateProofOfWork(uint nBits)
        {
          int nextHeight = Probe.GetHeight() + 1;
          if (nBits != TargetManager.GetNextTargetBits(Probe.Header, (uint)nextHeight))
          {
            throw new ChainException(ChainCode.INVALID);
          }
        }
        void ValidateTimeStamp(uint unixTimeSeconds)
        {
          if (unixTimeSeconds <= GetMedianTimePast(Probe.Header))
          {
            throw new ChainException(ChainCode.INVALID);
          }
        }
        void ValidateUniqueness(byte[] hash)
        {
          if (
            Probe.Header.HeadersNext != null &&
            Probe.Header.HeadersNext.Select(h => Probe.GetHeaderHash(h)).Contains(hash, new EqualityComparerByteArray()))
          {
            throw new ChainException(ChainCode.DUPLICATE);
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
}
