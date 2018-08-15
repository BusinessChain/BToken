using System;
using System.Collections.Generic;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      static class TargetManager
      {
        const int RETARGETING_BLOCK_INTERVAL = 2016;
        const ulong RETARGETING_TIMESPAN_INTERVAL = 14 * 24 * 60 * 60; // two weeks in seconds

        static readonly UInt256 DIFFICULTY_1_TARGET = new UInt256("00000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        const double MAX_TARGET = 2.695994666715064E67;

        public static UInt32 GetNextTargetBits(SocketProbe probe)
        {
          uint nextHeight = probe.GetHeight() + 1;
          if ((nextHeight % RETARGETING_BLOCK_INTERVAL) == 0)
          {
            return GetTargetBits(GetNextTarget(probe));
          }

          return probe.Block.NBits;
        }
        static UInt256 GetNextTarget(SocketProbe probe)
        {
          ChainBlock headerIntervalStart = GetBlockPrevious(probe.Block, RETARGETING_BLOCK_INTERVAL - 1);
          ulong actualTimespan = Limit(probe.Block.UnixTimeSeconds - headerIntervalStart.UnixTimeSeconds);
          UInt256 oldTarget = GetTarget(probe.Block.NBits);

          return oldTarget.multiplyBy(actualTimespan).divideBy(RETARGETING_TIMESPAN_INTERVAL);
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
        static UInt32 GetTargetBits(UInt256 target)
        {
          return 0;
        }

        public static UInt256 GetTarget(UInt32 nBits)
        {
          int expBits = ((int)nBits & 0x7F000000) >> 24;
          UInt32 factorBits = nBits & 0x00FFFFFF;

          if (expBits < 3)
          {
            factorBits >>= (3 - expBits) * 8;
          }

          var targetBytes = new List<byte>();

          for (int i = expBits - 3; i > 0; i--)
          {
            targetBytes.Add(0x00);
          }
          targetBytes.Add((byte)(factorBits & 0xFF));
          targetBytes.Add((byte)((factorBits & 0xFF00) >> 8));
          targetBytes.Add((byte)((factorBits & 0xFF0000) >> 16));

          return new UInt256(targetBytes.ToArray());
        }

        
        public static double GetDifficulty(UInt32 nBits)
        {
          return MAX_TARGET / (double)GetTarget(nBits);
        }
      }
    }
  }
}
