using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  partial class UTXO
  {
    class UTXOIndex
  {
      static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UTXOArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);


      public UTXOIndex()
      {
      }
      // mehrere index files

      //public async Task IndexOutputAsync(string outputReference, UInt256 blockHeaderHash)
      //{
      //  // write cache

      //  using (FileStream fileStream = CreateFile(blockHeaderHash))
      //  {
      //    byte[] headerBytes = block.Header.GetBytes();
      //    byte[] txCount = VarInt.GetBytes(block.TXCount).ToArray();

      //    await fileStream.WriteAsync(headerBytes, 0, headerBytes.Length);
      //    await fileStream.WriteAsync(txCount, 0, txCount.Length);
      //    await fileStream.WriteAsync(block.Payload, 0, block.Payload.Length);
      //  }
      //}

    }
  }
}
