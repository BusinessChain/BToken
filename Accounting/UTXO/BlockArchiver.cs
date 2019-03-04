﻿using System;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;

namespace BToken.Accounting
{
  partial class BlockArchiver
  {
    static string PathBlockArchive = "I:\\BlockArchive";
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

    public static async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
    {
      string fileRootPath = CreateFileRootPath(hash);
      string filePath = Path.Combine(fileRootPath, hash.ToString());

      using (FileStream fileStream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 8192,
        useAsync: true))
      {
        byte[] blockBytes = await ReadBytesAsync(fileStream);
        return NetworkBlock.ReadBlock(blockBytes);
      }
    }
    static string CreateFileRootPath(UInt256 blockHash)
    {
      byte[] prefixBlockFolderBytes = blockHash.GetBytes().Take(PrefixBlockFolderBytes).ToArray();
      Array.Reverse(prefixBlockFolderBytes);
      string blockHashIndex = new SoapHexBinary(prefixBlockFolderBytes).ToString();
      return Path.Combine(PathBlockArchive, blockHashIndex);
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