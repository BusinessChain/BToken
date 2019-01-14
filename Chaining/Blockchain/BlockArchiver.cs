using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class BlockArchiver
    {
      static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      Blockchain Blockchain;


      public BlockArchiver(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      FileStream CreateFile(UInt256 hash)
      {
        string filename = hash.ToString();
        string fileRootPath = GenerateRootPath(filename);

        DirectoryInfo dir = Directory.CreateDirectory(fileRootPath);

        string filePath = Path.Combine(fileRootPath, filename);

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None);
      }

      public async Task ArchiveBlockAsync(NetworkBlock block, UInt256 hash)
      {
        // write cache

        using (FileStream fileStream = CreateFile(hash))
        {
          byte[] headerBytes = block.Header.GetBytes();
          byte[] txCount = VarInt.GetBytes(block.TXCount).ToArray();

          await fileStream.WriteAsync(headerBytes, 0, headerBytes.Length);
          await fileStream.WriteAsync(txCount, 0, txCount.Length);
          await fileStream.WriteAsync(block.Payload, 0, block.Payload.Length);
        }
      }

      public async Task<NetworkBlock> ReadBlockAsync(UInt256 blockHeaderHashRequested)
      {
        // read cache

        using (MemoryStream blockStream = OpenBlockStream(blockHeaderHashRequested.GetBytes()))
        {
          NetworkBlock block = await NetworkBlock.ReadBlockAsync(blockStream);
          return block;
        }
      }

      static MemoryStream OpenBlockStream(byte[] blockHeaderHashBytes)
      {
        string filePath = Path.Combine(GenerateRootPath(filename), filename);

        var fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read);
      }

      static string GenerateRootPath(string filename)
      {
        string firstHexByte = filename.Substring(62, 2);
        string secondHexByte = filename.Substring(60, 2);

        return Path.Combine(
          RootDirectory.Name,
          firstHexByte,
          secondHexByte);
      }

      public bool BlockExists(UInt256 blockHeaderHash)
      {
        string fileName = blockHeaderHash.ToString();
        string filePath = Path.Combine(GenerateRootPath(fileName), fileName);

        return File.Exists(filePath);
      }
    }
  }
}