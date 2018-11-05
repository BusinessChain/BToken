using System.Diagnostics;

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
    partial class BlockArchiver
    {
      class ArchiveLoader : IDisposable
      {
        Blockchain Blockchain;
        List<ArchiveShardLoader> ShardLoaders = new List<ArchiveShardLoader>();
        
        public ArchiveLoader(Blockchain blockchain)
        {
          Blockchain = blockchain;
        }


        void InsertShardBlocks(ArchiveShardLoader shardLoader)
        {
          BlockStore blockStore;
          NetworkBlock block = shardLoader.PeekBlockNext(out blockStore);


          while (block != null)
          {
            try
            {
              Debug.WriteLine("insert block: "+ Blockchain.GetHeight());
              Blockchain.InsertHeader(block.Header);
              shardLoader.DispatchBlockNext();
            }
            catch (BlockchainException ex)
            {
              Debug.WriteLine("BlockchainException: " + ex.ErrorCode + ", Block height: " + Blockchain.GetHeight());
              switch (ex.ErrorCode)
              {
                case BlockCode.ORPHAN:
                  return;

                case BlockCode.DUPLICATE:
                  shardLoader.DispatchBlockNext();
                  break;

                default:
                  throw ex;
              }
            }

            block = shardLoader.PeekBlockNext(out blockStore);
          }

          shardLoader.Dispose();
          ShardLoaders.Remove(shardLoader);
        }

        public void Load()
        {
          ShardLoaders = GetArchiveShardLoaders();

          int i = 0;
          while (ShardLoaders.Count > 0)
          {
            InsertShardBlocks(ShardLoaders[i]);

            i++;
            if (i == ShardLoaders.Count)
            {
              i = 0;
            }

          }
        }

        static List<ArchiveShardLoader> GetArchiveShardLoaders()
        {
          string[] shardDirectories = Directory.GetDirectories(ArchiveRootPath, "Shard?");
          return shardDirectories.Select(s => new ArchiveShardLoader(s)).ToList();
        }

        public void Dispose()
        {
          ShardLoaders.ForEach(s => s.Dispose());
        }
      }
    }
  }
}
