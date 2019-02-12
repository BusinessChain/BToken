using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOArchiver
    {
      static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      UTXO UTXO;

      public UTXOArchiver(UTXO uTXO)
      {
        UTXO = uTXO;
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
      FileStream CreateFile(UInt256 hash)
      {
        string filePath = CreateFilePath(hash);

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None);
      }

      public void DeleteBlock(UInt256 hash)
      {
        string filePath = CreateFilePath(hash);
        File.Delete(filePath);
      }

      public async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
      {
        // read cache

        string filePath = CreateFilePath(hash);

        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read))
        {
          return await NetworkBlock.ReadBlockAsync(fileStream);
        }
      }
      string CreateFilePath(UInt256 blockHash)
      {
        string filename = blockHash.ToString();
        string blockHashIndex = new SoapHexBinary(blockHash.GetBytes().Take(2).ToArray()).ToString();

        return Path.Combine(
          RootDirectory.Name,
          blockHashIndex,
          blockHash.ToString());
      }
    }
  }
}