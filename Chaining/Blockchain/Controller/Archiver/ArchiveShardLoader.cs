using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Archiver
    {
      class ArchiveShardLoader : IDisposable
      {
        FileStream FileStream;
        FileID FileID;

        NetworkBlock BlockNext;


        public ArchiveShardLoader(string shardRootPath)
        {
          string[] directories = Directory.GetDirectories(shardRootPath);

          FileID = new FileID
          {
            DirectoryIndex = 0,
            FileIndex = 0
          };
        }

        public NetworkBlock PeekBlockNext(out BlockStore blockStore)
        {
          if(BlockNext == null)
          {
            try
            {
              NetworkBlock networkBlock = ParseNetworkBlock(FileStream);
              if (networkBlock == null)
              {
                // open next file
                // if not exist, return null
              }
            }
            catch(Exception ex)
            {
              Console.WriteLine(ex.Message);
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