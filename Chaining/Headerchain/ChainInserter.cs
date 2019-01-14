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
    partial class Headerchain
    {
      class ChainInserter : ChainProbe, IDisposable
      {
        Headerchain Headerchain;
        public double AccumulatedDifficulty;

        readonly object IsDispatchedLOCK = new object();
        bool IsDispatched = false;


        public ChainInserter(Headerchain headerchain)
          : base(headerchain.MainChain)
        {
          Headerchain = headerchain;

          Headerchain.SignalInserterAvailable.Post(true);
        }

        protected override void Initialize()
        {
          base.Initialize();
          AccumulatedDifficulty = Chain.AccumulatedDifficulty;
        }
        protected override void Push()
        {
          base.Push();
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Header.NetworkHeader.NBits);
        }
        
        public Chain InsertHeader(NetworkHeader networkHeader, UInt256 headerHash)
        {
          FindPreviousHeader(networkHeader);

          ValidateHeader(networkHeader, headerHash);

          var chainHeader = new ChainHeader(networkHeader, Header);

          Header.HeadersNext.Add(chainHeader);

          if (IsTip())
          {
            Chain.ExtendChain(chainHeader, headerHash);

            if (Chain == Headerchain.MainChain)
            {
              Headerchain.Locator.Update();
              Headerchain.UpdateHeaderIndex();
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
        void FindPreviousHeader(NetworkHeader header)
        {
          Chain = Headerchain.MainChain;
          if (GoTo(header.HashPrevious, Headerchain.MainChain.HeaderRoot)) { return; }

          foreach (Chain chain in Headerchain.SecondaryChains)
          {
            Chain = chain;
            if (GoTo(header.HashPrevious, chain.HeaderRoot)) { return; }
          }

          throw new ChainException(ChainCode.ORPHAN);
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

          uint highestCheckpointHight = Headerchain.Checkpoints.Max(x => x.Height);
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
        bool ValidateBlockLocation(uint height, UInt256 hash)
        {
          ChainLocation checkpoint = Headerchain.Checkpoints.Find(c => c.Height == height);
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
            throw new ChainException(ChainCode.INVALID);
          }
        }
        void ValidateTimeStamp(uint unixTimeSeconds)
        {
          if (unixTimeSeconds <= GetMedianTimePast(Header))
          {
            throw new ChainException(ChainCode.INVALID);
          }
        }
        void ValidateUniqueness(UInt256 hash)
        {
          if (Header.HeadersNext.Any(h => GetHeaderHash(h).IsEqual(hash)))
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

        ChainHeader ConnectHeader(NetworkHeader header)
        {
          var chainHeader = new ChainHeader(header, Header);
          Header.HeadersNext.Add(chainHeader);
          return chainHeader;
        }
        Chain ForkChain(UInt256 headerHash)
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
}
