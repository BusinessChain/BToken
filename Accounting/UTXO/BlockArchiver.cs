using System.Diagnostics;
using System;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;

namespace BToken.Accounting
{
  partial class BlockArchiver
  {
    static string[] ShardPaths = {
    "I:\\BlockArchive",
    "J:\\BlockArchive",
    "D:\\BlockArchive",
    "C:\\BlockArchive"
    };
    const int PrefixBlockFolderBytes = 2;


    public static async Task ArchiveBlockAsync(Block block)
    {
      // write cache

      using (FileStream file = CreateFile(block.HeaderHash))
      {
        byte[] headerBytes = block.Header.GetBytes();
        byte[] txCount = VarInt.GetBytes(block.TXs.Count).ToArray();

        await file.WriteAsync(headerBytes, 0, headerBytes.Length);
        await file.WriteAsync(txCount, 0, txCount.Length);

        for (int t = 0; t < block.TXs.Count; t++)
        {
          byte[] txBytes = block.TXs[t].GetBytes();
          await file.WriteAsync(txBytes, 0, txBytes.Length);
        }
      }
    }
    static FileStream CreateFile(UInt256 hash)
    {
      string fileRootPath = CreateFileRootPath(hash);
      Directory.CreateDirectory(fileRootPath);
      string filePath = Path.Combine(fileRootPath, hash.ToString());

      return new FileStream(
        filePath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 8192,
        useAsync: true);
    }

    public static void DeleteBlock(UInt256 hash)
    {
      string fileRootPath = CreateFileRootPath(hash);
      string filePath = Path.Combine(fileRootPath, hash.ToString());
      File.Delete(filePath);
    }
    public static bool Exists(UInt256 hash, out string filePath)
    {
      string fileRootPath = CreateFileRootPath(hash);
      filePath = Path.Combine(fileRootPath, hash.ToString());

      return File.Exists(filePath);
    }
    public static bool Exists(int batchIndex, out string filePath)
    {
      string shardPath = ShardPaths[batchIndex % ShardPaths.Length];
      filePath = Path.Combine(shardPath, batchIndex.ToString());

      return File.Exists(filePath);
    }
    public static async Task<byte[]> ReadBlockBatchAsync(string filePath)
    {
      using (FileStream fileStream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 8192,
        useAsync: true))
      {
        return await ReadBytesAsync(fileStream); ;
      }
    }
    static string CreateFileRootPath(UInt256 blockHash)
    {
      byte[] prefixBlockFolderBytes = blockHash.GetBytes().Take(PrefixBlockFolderBytes).ToArray();
      Array.Reverse(prefixBlockFolderBytes);
      string blockHashIndex = new SoapHexBinary(prefixBlockFolderBytes).ToString();
      byte byteID = prefixBlockFolderBytes.First();
      string shardPath = GetShardPath(byteID);
      return Path.Combine(GetShardPath(byteID), blockHashIndex);
    }
    static string GetShardPath(byte byteID)
    {
      return ShardPaths[byteID % ShardPaths.Length];
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