using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    class HeaderValidator
    {
      uint HighestCheckpointHight;
      List<ChainLocation> Checkpoints;
      

      public HeaderValidator(List<ChainLocation> checkpoints)
      {
        Checkpoints = checkpoints;

        HighestCheckpointHight = checkpoints.Max(x => x.Height);
      }

      public void ValidateHeader(ChainProbe probe, NetworkHeader header, out UInt256 headerHash)
      {
        if (probe == null)
        {
          throw new ChainException(BlockCode.ORPHAN);
        }

        ValidateTimeStamp(probe, header.UnixTimeSeconds);

        headerHash = new UInt256(Hashing.SHA256d(header.GetBytes()));

        ValidateCheckpoint(probe, headerHash);
        ValidateUniqueness(probe, headerHash);
        ValidateProofOfWork(probe, header.NBits, headerHash);
      }
      void ValidateCheckpoint(ChainProbe probe, UInt256 headerHash)
      {
        uint nextBlockHeight = probe.GetHeight() + 1;

        bool chainLongerThanHighestCheckpoint = probe.Chain.Height >= HighestCheckpointHight;
        bool nextHeightBelowHighestCheckpoint = !(nextBlockHeight > HighestCheckpointHight);

        if (chainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
        {
          throw new ChainException(BlockCode.INVALID);
        }

        if (!ValidateBlockLocation(nextBlockHeight, headerHash))
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
      void ValidateProofOfWork(ChainProbe probe, uint nBits, UInt256 headerHash)
      {
        if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(nBits)))
        {
          throw new ChainException(BlockCode.INVALID);
        }

        if (nBits != TargetManager.GetNextTargetBits(probe))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateTimeStamp(ChainProbe probe, uint unixTimeSeconds)
      {
        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        bool IsTimestampPremature = (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
        if (IsTimestampPremature)
        {
          throw new ChainException(BlockCode.PREMATURE);
        }

        if (unixTimeSeconds <= GetMedianTimePast(probe))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateUniqueness(ChainProbe probe, UInt256 hash)
      {
        if (probe.Header.HeadersNext.Any(b => probe.Chain.GetHeaderHash(b).IsEqual(hash)))
        {
          throw new ChainException(BlockCode.DUPLICATE);
        }
      }
      uint GetMedianTimePast(ChainProbe probe)
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();
        ChainHeader block = probe.Header;

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(block.NetworkHeader.UnixTimeSeconds);

          if (block.HeaderPrevious == null)
          { break; }

          block = block.HeaderPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }
    }
  }
}
