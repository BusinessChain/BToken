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
            

      public BlockArchiver(Blockchain blockchain, INetwork network)
      { }

      FileStream CreateFile(UInt256 hash)
      {
        string filename = hash.ToString();
        string fileRootPath = ConvertToRootPath(filename);

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

      public async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
      {
        // read cache

        using (FileStream blockFileStream = OpenFile(hash.ToString()))
        {
          byte[] blockBytes = new byte[blockFileStream.Length];
          int i = await blockFileStream.ReadAsync(blockBytes, 0, (int)blockFileStream.Length);

          return NetworkBlock.ParseBlock(blockBytes);
        }
      }

      static FileStream OpenFile(string filename)
      {
        string fileRootPath = ConvertToRootPath(filename);
        string filePath = Path.Combine(fileRootPath, filename);

        return new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read);
      }

      static string ConvertToRootPath(string filename)
      {
        string firstHexByte = filename.Substring(62, 2);
        string secondHexByte = filename.Substring(60, 2);

        return Path.Combine(
          RootDirectory.Name,
          firstHexByte,
          secondHexByte);
      }

    }
  }
}