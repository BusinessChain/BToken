using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain : IBlockchain
  {
    partial class BlockArchiver
    {
      class ArchiveShardLoader : IDisposable
      {
        FileStream FileStream;
        FileID FileID;
        
        NetworkBlock BlockNext;


        public ArchiveShardLoader(string shardRootPath)
        {
          uint shardIndex = uint.Parse(Regex.Match(shardRootPath, @"\d+$").Value);

          FileID = new FileID
          {
            ShardIndex = shardIndex,
            DirectoryIndex = 0,
            FileIndex = 0
          };

          FileStream = OpenFile(FileID);
        }

        public NetworkBlock PeekBlockNext(out BlockStore blockStore)
        {
          if (BlockNext == null)
          {
            BlockNext = ParseNetworkBlock(FileStream);
            if (BlockNext == null)
            {
              FileStream.Dispose();

              IncrementFileID(ref FileID);

              try
              {
                FileStream = OpenFile(FileID);
              }
              catch (FileNotFoundException)
              {
                blockStore = null;
                return null;
              }

              BlockNext = ParseNetworkBlock(FileStream);
            }
          }

          blockStore = new BlockStore(FileID);
          return BlockNext;
        }

        public void DispatchBlockNext()
        {
          BlockNext = null;
        }

        public void Dispose()
        {
          FileStream.Dispose();
        }
      }
    }
  }
}