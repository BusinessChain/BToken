using System;

namespace BToken.Chaining
{
  partial class ChainHeader : ChainLink
  {
    static class TargetManager
    {
      const int RETARGETING_BLOCK_INTERVAL = 2016;
      const ulong RETARGETING_TIMESPAN_INTERVAL = 14 * 24 * 60 * 60; // two weeks in seconds

      const string DIFFICULTY_1_TARGET = "00000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
      static readonly UInt256 MaxTarget = new UInt256(DIFFICULTY_1_TARGET);
      const double MAX_TARGET = 2.695994666715064E67;


      public static UInt256 getNextTarget(ChainHeader headerPrevious)
      {
        uint nextHeight = headerPrevious.Height + 1;

        if ((nextHeight % RETARGETING_BLOCK_INTERVAL) != 0)
        {
          return headerPrevious.Target;
        }

        ChainHeader headerIntervalStart = headerPrevious.getHeaderPrevious(RETARGETING_BLOCK_INTERVAL - 1);

        ulong actualTimespan = limit(headerPrevious.UnixTimeSeconds - headerIntervalStart.UnixTimeSeconds);
        return calculateTarget(headerPrevious.Target, actualTimespan);
      }
      static ulong limit(ulong actualTimespan)
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
      static UInt256 calculateTarget(UInt256 oldTarget, ulong actualTimespan)
      {
        UInt256 newTarget = oldTarget.multiplyBy(actualTimespan).divideBy(RETARGETING_TIMESPAN_INTERVAL);

        return UInt256.Min(MaxTarget, newTarget);
      }

      public static double getDifficulty(UInt256 target)
      {
        return MAX_TARGET / (double)target;
      }
    }
  }
}
