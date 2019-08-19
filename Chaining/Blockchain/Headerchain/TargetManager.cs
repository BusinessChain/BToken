using System;
using System.Collections.Generic;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public partial class Headerchain
    {
      static class TargetManager
      {
        const int RETARGETING_BLOCK_INTERVAL = 2016;
        const ulong RETARGETING_TIMESPAN_INTERVAL_SECONDS = 14 * 24 * 60 * 60;

        static readonly UInt256 DIFFICULTY_1_TARGET = new UInt256("00000000FFFF0000000000000000000000000000000000000000000000000000");
        const double MAX_TARGET = 2.695994666715064E67;

        public static uint GetNextTargetBits(ChainHeader header, uint nextHeight)
        {
          uint nextTargetBits;

          if ((nextHeight % RETARGETING_BLOCK_INTERVAL) == 0)
          {
            UInt256 nextTarget = GetNextTarget(header);
            nextTargetBits = nextTarget.GetCompact();
          }
          else
          {
            nextTargetBits = header.NetworkHeader.NBits;
          }

          return nextTargetBits;

        }
        static UInt256 GetNextTarget(ChainHeader header)
        {
          ChainHeader headerIntervalStart = GetBlockPrevious(header, RETARGETING_BLOCK_INTERVAL - 1);
          ulong actualTimespan = Limit(header.NetworkHeader.UnixTimeSeconds - headerIntervalStart.NetworkHeader.UnixTimeSeconds);
          UInt256 targetOld = UInt256.ParseFromCompact(header.NetworkHeader.NBits);

          UInt256 targetNew = targetOld.MultiplyBy(actualTimespan).DivideBy(RETARGETING_TIMESPAN_INTERVAL_SECONDS);

          return UInt256.Min(DIFFICULTY_1_TARGET, targetNew);
        }
        static ChainHeader GetBlockPrevious(ChainHeader block, uint depth)
        {
          if (depth == 0 || block.HeaderPrevious == null)
          {
            return block;
          }

          return GetBlockPrevious(block.HeaderPrevious, --depth);
        }
        static ulong Limit(ulong actualTimespan)
        {
          if (actualTimespan < RETARGETING_TIMESPAN_INTERVAL_SECONDS / 4)
          {
            return RETARGETING_TIMESPAN_INTERVAL_SECONDS / 4;
          }

          if (actualTimespan > RETARGETING_TIMESPAN_INTERVAL_SECONDS * 4)
          {
            return RETARGETING_TIMESPAN_INTERVAL_SECONDS * 4;
          }

          return actualTimespan;
        }

        public static double GetDifficulty(UInt32 nBits)
        {
          double difficulty = MAX_TARGET / (double)UInt256.ParseFromCompact(nBits);

          return difficulty;
        }
      }
    }
  }
}
