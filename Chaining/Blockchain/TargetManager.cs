﻿using System;
using System.Collections.Generic;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    static class TargetManager
    {
      const int RETARGETING_BLOCK_INTERVAL = 2016;
      const ulong RETARGETING_TIMESPAN_INTERVAL_SECONDS = 14 * 24 * 60 * 60;

      static readonly UInt256 DIFFICULTY_1_TARGET =
        new UInt256("00000000FFFF0000000000000000000000000000000000000000000000000000");
      

      public static uint GetNextTargetBits(Header header, uint height)
      {
        if ((height % RETARGETING_BLOCK_INTERVAL) == 0)
        {
          UInt256 nextTarget = GetNextTarget(header);
          
          return nextTarget.GetCompact();
        }
        else
        {
          return header.NBits;
        }
      }
      static UInt256 GetNextTarget(Header header)
      {
        Header headerIntervalStart = GetHeaderPrevious(
          header, 
          RETARGETING_BLOCK_INTERVAL - 1);
        
        ulong actualTimespan = Limit(
          header.UnixTimeSeconds - 
          headerIntervalStart.UnixTimeSeconds);

        UInt256 targetOld = UInt256.ParseFromCompact(header.NBits);

        UInt256 targetNew = targetOld
          .MultiplyBy(actualTimespan)
          .DivideBy(RETARGETING_TIMESPAN_INTERVAL_SECONDS);

        return UInt256.Min(DIFFICULTY_1_TARGET, targetNew);
      }

      static Header GetHeaderPrevious(Header header, uint depth)
      {
        if (depth == 0 || header.HeaderPrevious == null)
        {
          return header;
        }

        return GetHeaderPrevious(header.HeaderPrevious, --depth);
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
    }
  }
}
