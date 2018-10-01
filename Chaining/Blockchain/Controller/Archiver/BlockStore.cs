using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class BlockArchiver
  {
    public struct FileID
    {
      public uint ShardIndex;
      public uint DirectoryIndex;
      public uint FileIndex;
    }

    public class BlockStore
    {
      public FileID FileID;
    }
  }
}
