using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class BlockArchiver
    {
      string BlockArchivePath = "J:\\BlockArchivePartitioned";

      const int PrefixBlockFolderBytes = 2;

      public async Task ArchiveBatchAsync(UTXOTable.UTXOBatch batch)
      {
        try
        {
          using (FileStream file = CreateFile(batch.BatchIndex))
          {
            foreach (Block block in batch.Blocks)
            {
              await file.WriteAsync(block.Buffer, 0, block.Buffer.Length);
            }
          }
        }
        catch(Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
      FileStream CreateFile(int filePartitionIndex)
      {
        Directory.CreateDirectory(BlockArchivePath);
        string filePath = Path.Combine(BlockArchivePath, "p" + filePartitionIndex.ToString());

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 1048576,
          useAsync: true);
      }

      public async Task<byte[]> ReadBlockBatchAsync(int batchIndex)
      {
        string filePath = Path.Combine(BlockArchivePath, "p" + batchIndex);

        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 1048576,
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