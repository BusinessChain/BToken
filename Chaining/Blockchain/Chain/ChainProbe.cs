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
        public ChainBlock Block;
        public UInt256 Hash;
        public double AccumulatedDifficulty;
        public uint Depth;


        public ChainProbe(
          ChainBlock block,
          UInt256 hash,
          double accumulatedDifficulty,
          uint depth)
        {
          Block = block;
          Hash = hash;
          AccumulatedDifficulty = accumulatedDifficulty;
          Depth = depth;
        }
      }
    }
  }
}
