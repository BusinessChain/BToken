using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Chain
    {
      public class ChainProbe
      {
        Chain Chain;

        public ChainBlock Block;
        public UInt256 Hash;
        public double AccumulatedDifficulty;
        public uint Depth;


        public ChainProbe(Chain chain)
        {
          Chain = chain;

          Initialize();
        }
        
        public void Initialize()
        {
          Block = Chain.Socket.BlockTip;
          Hash = Chain.Socket.BlockTipHash;
          AccumulatedDifficulty = Chain.Socket.AccumulatedDifficulty;
          Depth = 0;
        }

        public bool GetAtBlock(UInt256 hash)
        {
          Initialize();

          while (true)
          {
            if (IsHash(hash))
            {
              return true;
            }

            if (IsGenesis())
            {
              return false;
            }

            Push();
          }
        }

        public void Push()
        {
          Hash = Block.Header.HashPrevious;
          Block = Block.BlockPrevious;
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

          Depth++;
        }

        public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
        public bool IsTip() => Block == Chain.Socket.BlockTip;
        public bool IsGenesis() => Block == Chain.Socket.BlockGenesis;
        public uint GetHeight() => Chain.GetHeight() - Depth;

      }
    }
  }
}
