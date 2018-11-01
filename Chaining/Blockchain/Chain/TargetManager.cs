using System;
using System.Collections.Generic;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Chain
    {
      static class TargetManager
      {
        const int RETARGETING_BLOCK_INTERVAL = 2016;
        const ulong RETARGETING_TIMESPAN_INTERVAL = 14 * 24 * 60 * 60; // two weeks in seconds

        static readonly UInt256 DIFFICULTY_1_TARGET = new UInt256("00000000FFFF0000000000000000000000000000000000000000000000000000");
        const double MAX_TARGET = 2.695994666715064E67;

        public static UInt32 GetNextTargetBits(ChainProbe probe)
        {
          uint nextHeight = probe.GetHeight() + 1;
          if ((nextHeight % RETARGETING_BLOCK_INTERVAL) == 0)
          {
            UInt256 nextTarget = GetNextTarget(probe);
            UInt32 nextTargetBits = nextTarget.GetCompact();
            return nextTargetBits;
          }

          return probe.Block.Header.NBits;
        }
        static UInt256 GetNextTarget(ChainProbe probe)
        {
          ChainBlock headerIntervalStart = GetBlockPrevious(probe.Block, RETARGETING_BLOCK_INTERVAL - 1);
          ulong actualTimespan = Limit(probe.Block.Header.UnixTimeSeconds - headerIntervalStart.Header.UnixTimeSeconds);
          UInt256 targetOld = UInt256.ParseFromCompact(probe.Block.Header.NBits);

          UInt256 targetNew = targetOld.MultiplyBy(actualTimespan).DivideBy(RETARGETING_TIMESPAN_INTERVAL);

          return UInt256.Min(DIFFICULTY_1_TARGET, targetNew);
        }
        static ulong Limit(ulong actualTimespan)
        {
          if (actualTimespan < RETARGETING_TIMESPAN_INTERVAL / 4)
          {
            return RETARGETING_TIMESPAN_INTERVAL / 4;
          }

          if (actualTimespan > RETARGETING_TIMESPAN_INTERVAL * 4)
          {
            return RETARGETING_TIMESPAN_INTERVAL * 4;
          }

          return actualTimespan;
        }

        public static double GetDifficulty(UInt32 nBits)
        {
          return MAX_TARGET / (double)UInt256.ParseFromCompact(nBits);
        }
      }
    }
  }
}
