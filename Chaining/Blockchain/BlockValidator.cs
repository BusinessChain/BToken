using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class BlockValidator
    {
      uint HighestCheckpointHight;
      List<BlockLocation> Checkpoints;

      public BlockValidator(List<BlockLocation> checkpoints)
      {
        Checkpoints = checkpoints;
        HighestCheckpointHight = checkpoints.Max(x => x.Height);
      }

      public void Validate(ChainProbe probe, ChainBlock block, out UInt256 headerHash)
      {
        if (probe == null)
        {
          throw new BlockchainException(BlockCode.ORPHAN);
        }

        ValidateTimeStamp(probe, block.Header.UnixTimeSeconds);

        headerHash = new UInt256(Hashing.SHA256d(block.Header.GetBytes()));

        ValidateCheckpoint(probe, headerHash);

        ValidateUniqueness(probe, headerHash);

        ValidateProofOfWork(probe, block.Header.NBits, headerHash);

      }
      void ValidateCheckpoint(ChainProbe probe, UInt256 headerHash)
      {
        uint nextBlockHeight = probe.GetHeight() + 1;

        bool chainLongerThanHighestCheckpoint = probe.Chain.Height >= HighestCheckpointHight;
        bool nextHeightBelowHighestCheckpoint = !(nextBlockHeight > HighestCheckpointHight);

        if (chainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
        {
          throw new BlockchainException(BlockCode.INVALID);
        }

        if (!ValidateBlockLocation(nextBlockHeight, headerHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      bool ValidateBlockLocation(uint height, UInt256 hash)
      {
        BlockLocation checkpoint = Checkpoints.Find(c => c.Height == height);
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
          throw new BlockchainException(BlockCode.INVALID);
        }

        if (nBits != TargetManager.GetNextTargetBits(probe))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void ValidateTimeStamp(ChainProbe probe, uint unixTimeSeconds)
      {
        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        bool IsTimestampPremature = (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
        if (IsTimestampPremature)
        {
          throw new BlockchainException(BlockCode.PREMATURE);
        }

        if (unixTimeSeconds <= GetMedianTimePast(probe))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void ValidateUniqueness(ChainProbe probe, UInt256 hash)
      {
        if (probe.Block.BlocksNext.Any(b => probe.Chain.GetHeaderHash(b).IsEqual(hash)))
        {
          throw new BlockchainException(BlockCode.DUPLICATE);
        }
      }
      uint GetMedianTimePast(ChainProbe probe)
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();
        ChainBlock block = probe.Block;

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(block.Header.UnixTimeSeconds);

          if (block.BlockPrevious == null)
          { break; }

          block = block.BlockPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }
    }
  }
}
