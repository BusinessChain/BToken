using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

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
          if (Hash.IsEqual(hash))
          {
            return true;
          }

          if (Block == Chain.BlockRoot)
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

      public void ConnectHeader(NetworkHeader header)
      {
        var block = new ChainBlock(header);

        block.BlockPrevious = Block;
        Block.BlocksNext.Add(block);
      }
      public void ExtendChain(UInt256 headerHash)
      {
        ChainBlock block = Block.BlocksNext.Last();
        Chain.ExtendChain(block, headerHash);
      }
      public void ForkChain(UInt256 headerHash)
      {
        ChainBlock block = Block.BlocksNext.Last();
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
      
      public bool IsTip() => Block == Chain.BlockTip;
      public uint GetHeight() => Chain.Height - Depth;

    }
  }
}
