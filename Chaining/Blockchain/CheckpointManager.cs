using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    class CheckpointManager
    {
      public uint HighestCheckpointHight { get; private set; }
      List<BlockLocation> Checkpoints;


      public CheckpointManager(List<BlockLocation> checkpoints)
      {
        Checkpoints = checkpoints;
        HighestCheckpointHight = checkpoints.Max(x => x.Height);
      }

      public bool ValidateBlockLocation(uint height, UInt256 hash)
      {
        BlockLocation checkpoint = Checkpoints.Find(c => c.Height == height);
        if (checkpoint != null)
        {
          return checkpoint.Hash.IsEqual(hash);
        }

        return true;
      }
    }
  }
}
