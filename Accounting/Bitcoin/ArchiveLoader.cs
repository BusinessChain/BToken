using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class BlockArchiver
    {
      class ArchiveLoader : IDisposable
      {
        List<ArchiveShardLoader> ShardLoaders = new List<ArchiveShardLoader>();

        public ArchiveLoader()
        {
        }


        public void Load()
        {
          ShardLoaders = GetArchiveShardLoaders();

          int i = 0;
          while (ShardLoaders.Count > 0)
          {
            //InsertShardBlocks(ShardLoaders[i]);

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
