using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOArchiver
    {
      static string PathUTXOArchive = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UTXOArchive");
      const int PrefixBlockFolderBytes = 2;


      public static async Task ArchiveUTXOAsync(byte[] tXHash, byte[] uTXO)
      {
        using (FileStream file = CreateFile(tXHash))
        {
          byte[] outputsCount = VarInt.GetBytes(uTXO.Length - CountHeaderIndexBytes).ToArray();

          await file.WriteAsync(tXHash, 0, tXHash.Length);
          await file.WriteAsync(outputsCount, 0, outputsCount.Length);
        }
      }
      static FileStream CreateFile(byte[] tXHash)
      {
        string filePath = CreateFilePath(tXHash, out string fileRootPath);
        Directory.CreateDirectory(fileRootPath);

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None);
      }

      public static void DeleteUTXO(byte[] tXHash)
      {
        string filePath = CreateFilePath(tXHash, out string fileRootPath);
        File.Delete(filePath);
      }

      public static async Task<byte[]> ReadUTXOAsync(byte[] tXHash)
      {
        string filePath = CreateFilePath(tXHash, out string fileRootPath);
        
        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read))
        {
          return await UTXO.ReadUTXOAsync(fileStream);
        }
      }
      static string CreateFilePath(byte[] tXHash, out string fileRootPath)
      {
        byte[] prefixTXFolderBytes = tXHash.Take(PrefixBlockFolderBytes).ToArray();
        Array.Reverse(prefixTXFolderBytes);
        string tXHashIndex = new SoapHexBinary(prefixTXFolderBytes).ToString();
        fileRootPath = Path.Combine(PathUTXOArchive, tXHashIndex);

        Array.Reverse(tXHash);

        return Path.Combine(
          fileRootPath,
          new SoapHexBinary(tXHash).ToString());
      }
    }
  }
}
