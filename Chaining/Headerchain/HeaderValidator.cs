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

        ValidateTimeStamp(probe.Header, header.UnixTimeSeconds);

        headerHash = header.GetHeaderHash();

        ValidateCheckpoint(probe, headerHash);
        ValidateUniqueness(probe, headerHash);
        ValidateProofOfWork(probe, header.NBits, headerHash);
      }
      void ValidateCheckpoint(ChainProbe probe, UInt256 headerHash)
      {
        uint nextHeaderHeight = probe.GetHeight() + 1;

        bool chainLongerThanHighestCheckpoint = probe.Chain.Height >= HighestCheckpointHight;
        bool nextHeightBelowHighestCheckpoint = !(nextHeaderHeight > HighestCheckpointHight);

        if (chainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
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
      void ValidateProofOfWork(ChainProbe probe, uint nBits, UInt256 headerHash)
      {
        uint nextHeight = probe.GetHeight() + 1;
        if (nBits != TargetManager.GetNextTargetBits(probe.Header, nextHeight))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateTimeStamp(ChainHeader header, uint unixTimeSeconds)
      {
        if (unixTimeSeconds <= GetMedianTimePast(header))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }
      void ValidateUniqueness(ChainProbe probe, UInt256 hash)
      {
        if (probe.Header.HeadersNext.Any(h => probe.GetHeaderHash(h).IsEqual(hash)))
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
    }
  }
}
