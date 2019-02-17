using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOArchiver
    {
      static string ArchiveRootPath = "I:\\BlockArchive";
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      UTXO UTXO;


      public UTXOArchiver(UTXO uTXO)
      {
        UTXO = uTXO;
      }

      public async Task ArchiveBlockAsync(Block block)
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
      FileStream CreateFile(UInt256 hash)
      {
        string fileRootPath = CreateFileRootPath(hash);
        Directory.CreateDirectory(fileRootPath);
        string filePath = Path.Combine(fileRootPath, hash.ToString());

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None);
      }

      public void DeleteBlock(UInt256 hash)
      {
        string fileRootPath = CreateFileRootPath(hash);
        string filePath = Path.Combine(fileRootPath, hash.ToString());
        File.Delete(filePath);
      }

      public async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
      {
        // read cache

        string fileRootPath = CreateFileRootPath(hash);
        string filePath = Path.Combine(fileRootPath, hash.ToString());

        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read))
        {
          return await NetworkBlock.ReadBlockAsync(fileStream);
        }
      }
      string CreateFileRootPath(UInt256 blockHash)
      {
        byte[] lastTwoBytes = blockHash.GetBytes().Take(2).ToArray();
        Array.Reverse(lastTwoBytes);
        string blockHashIndex = new SoapHexBinary(lastTwoBytes).ToString();
        return Path.Combine(RootDirectory.FullName, blockHashIndex);
      }
    }
  }
}