using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class BlockArchiver
    {
      static string[] ShardPaths = {
    "J:\\BlockArchivePartitioned"};

      const int PrefixBlockFolderBytes = 2;

      public static async Task ArchiveBlocksAsync(
        List<Block> blocks,
        int filePartitionIndex)
      {
        using (FileStream file = CreateFile(filePartitionIndex))
        {
          foreach (Block block in blocks)
          {
            await file.WriteAsync(block.Buffer, 0, block.Buffer.Length);
          }
        }
      }
      static FileStream CreateFile(int filePartitionIndex)
      {
        string shardRootPath = ShardPaths[filePartitionIndex % ShardPaths.Length];
        Directory.CreateDirectory(shardRootPath);
        string filePath = Path.Combine(shardRootPath, "p" + filePartitionIndex.ToString());

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 8192,
          useAsync: true);
      }

      public static bool Exists(int batchIndex, out string filePath)
      {
        string shardPath = ShardPaths[batchIndex % ShardPaths.Length];
        filePath = Path.Combine(shardPath, "p" + batchIndex.ToString());

        return File.Exists(filePath);
      }
      public static async Task<byte[]> ReadBlockBatchAsync(string filePath)
      {
        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 4096,
          useAsync: true))
        {
          return await ReadBytesAsync(fileStream);
        }
      }

      static async Task<byte[]> ReadBytesAsync(Stream stream)
      {
        var buffer = new byte[stream.Length];

        int bytesToRead = buffer.Length;
        int offset = 0;
        while (bytesToRead > 0)
        {
          int chunkSize = await stream.ReadAsync(buffer, offset, bytesToRead);

          offset += chunkSize;
          bytesToRead -= chunkSize;
        }

        return buffer;
      }
    }
  }
}