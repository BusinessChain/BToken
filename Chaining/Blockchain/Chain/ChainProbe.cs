using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class ChainProbe
    {
      public Chain Chain;

      public ChainBlock Block;
      public UInt256 Hash;
      public double AccumulatedDifficulty;
      public uint Depth;


      public ChainProbe(Chain chain)
      {
        Chain = chain;

        Initialize();
      }

      void Initialize()
      {
        Block = Chain.BlockTip;
        Hash = Chain.BlockTipHash;
        AccumulatedDifficulty = Chain.AccumulatedDifficulty;
        Depth = 0;
      }

      public bool GotoBlock(UInt256 hash)
      {
        Initialize();

        while (true)
        {
          if (IsHash(hash))
          {
            return true;
          }

          if (IsRoot())
          {
            return false;
          }

          Push();
        }
      }

      void Push()
      {
        Hash = Block.Header.HashPrevious;
        Block = Block.BlockPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

        Depth++;
      }
      public void ConnectBlock(ChainBlock block)
      {
        block.BlockPrevious = Block;
        Block.BlocksNext.Add(block);
      }

      public void ForkChain(ChainBlock block, UInt256 headerHash)
      {
        ChainBlock blockHighestAssigned = block.BlockStore != null ? block : null;
        uint blockTipHeight = GetHeight() + 1;

        Chain = new Chain(
          blockTip: block,
          blockTipHash: headerHash,
          blockTipHeight: blockTipHeight,
          blockRoot: block,
          blockHighestAssigned: blockHighestAssigned,
          accumulatedDifficultyPrevious: AccumulatedDifficulty);

      }

      public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
      public bool IsTip() => Block == Chain.BlockTip;
      public bool IsRoot() => Block == Chain.BlockRoot;
      public uint GetHeight() => Chain.Height - Depth;

    }
  }
}
