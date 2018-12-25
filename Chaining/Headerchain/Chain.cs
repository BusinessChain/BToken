﻿using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class Chain
      {
        public ChainHeader HeaderTip { get; private set; }
        public UInt256 HeaderTipHash { get; private set; }
        public uint Height { get; private set; }
        public double AccumulatedDifficulty { get; private set; }
        public ChainHeader HeaderRoot { get; private set; }


        public Chain(
          ChainHeader headerRoot,
          uint height,
          double accumulatedDifficultyPrevious)
        {
          HeaderTip = headerRoot;
          HeaderTipHash = headerRoot.NetworkHeader.GetHeaderHash();
          Height = height;
          HeaderRoot = headerRoot;
          AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(headerRoot.NetworkHeader.NBits);
        }

        public void ExtendChain(ChainHeader header, UInt256 headerHash)
        {
          HeaderTip = header;
          HeaderTipHash = headerHash;
          Height++;
          AccumulatedDifficulty += TargetManager.GetDifficulty(header.NetworkHeader.NBits);
        }

        public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
      }
    }
  }
}