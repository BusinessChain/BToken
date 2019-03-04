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
    static class UTXOArchiver
    {
      static string PathUTXOArchive = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UTXOArchive");
      

      public static async Task ArchiveUTXOShardsAsync(Dictionary<byte[], byte[]>[] uTXOShards)
      {
        for(int i = 0; i < uTXOShards.Length; i++)
        {
          if(uTXOShards[i] == null)
          {
            continue;
          }

          Console.WriteLine("shard index: " + i);

          string fileName = new SoapHexBinary(new byte[] { (byte)i }).ToString();
          string filePath = Path.Combine(PathUTXOArchive, fileName);
          Directory.CreateDirectory(PathUTXOArchive);

          try
          {
            using (FileStream shardStreamNew = new FileStream(
             filePath + "_archiving",
             FileMode.Create,
             FileAccess.ReadWrite,
             FileShare.None,
             bufferSize: 8192,
             useAsync: true))
            {
              if (File.Exists(filePath))
              {
                using (FileStream shardStreamExisting = new FileStream(
                 filePath,
                 FileMode.Open,
                 FileAccess.Read,
                 FileShare.Read,
                 bufferSize: 8192,
                 useAsync: true))
                {
                  KeyValuePair<byte[], byte[]> uTXO = await ParseUTXOIndexAsync(shardStreamExisting);
                  while (uTXO.Key != null)
                  {
                    await shardStreamNew.WriteAsync(uTXO.Key, 0, uTXO.Key.Length);
                    await shardStreamNew.WriteAsync(uTXO.Value, 0, uTXO.Value.Length);

                    uTXO = await ParseUTXOIndexAsync(shardStreamExisting);
                  }
                }
              }

              foreach(KeyValuePair<byte[], byte[]> uTXO in uTXOShards[i])
              {
                byte[] uTXOLength = VarInt.GetBytes(uTXO.Value.Length).ToArray();
                
                await shardStreamNew.WriteAsync(uTXO.Key, 0, uTXO.Key.Length);
                await shardStreamNew.WriteAsync(uTXOLength, 0, uTXOLength.Length);
                await shardStreamNew.WriteAsync(uTXO.Value, 0, uTXO.Value.Length);
              }
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine("UTXO BatchArchiver threw exception: " + ex.Message);
          }

          File.Delete(filePath);
          File.Move(filePath + "_archiving", filePath);

        }
      }

      public static async Task DeleteUTXOAsync(byte[] tXHash)
      {
        string fileName = new SoapHexBinary(new byte[] { tXHash[0] }).ToString();
        string filePath = Path.Combine(PathUTXOArchive, fileName);

        if(!File.Exists(filePath))
        {
          return;
        }

        var uTXOsNotToBeDeletedKeys = new List<byte[]>();
        var uTXOsNotToBeDeletedValues = new List<byte[]>();
        bool anyDeletion = false;
        using (FileStream file = new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.ReadWrite,
          FileShare.None))
        {
          KeyValuePair<byte[], byte[]> uTXO = await ParseUTXOIndexAsync(file);
          while (uTXO.Key != null)
          {
            if(!EqualityComparerByteArray.IsEqual(uTXO.Key, tXHash))
            {
              uTXOsNotToBeDeletedKeys.Add(uTXO.Key);
              uTXOsNotToBeDeletedValues.Add(uTXO.Value);
            }
            else
            {
              anyDeletion = true;
            }
          }
        }

        if (anyDeletion)
        {
          File.Delete(filePath);

          if(uTXOsNotToBeDeletedKeys.Any())
          {
            using (FileStream file = new FileStream(
              filePath,
              FileMode.OpenOrCreate,
              FileAccess.Write,
              FileShare.None))
            {
              for(int i = 0; i < uTXOsNotToBeDeletedKeys.Count; i++)
              {
                await file.WriteAsync(uTXOsNotToBeDeletedKeys[i], 0, uTXOsNotToBeDeletedKeys[i].Length);
                await file.WriteAsync(uTXOsNotToBeDeletedValues[i], 0, uTXOsNotToBeDeletedValues[i].Length);
              }
            }
          }
        }
      }

      static async Task<KeyValuePair<byte[], byte[]>> ParseUTXOIndexAsync(FileStream fileStream)
      {
        if(fileStream.Position == fileStream.Length)
        {
          return new KeyValuePair<byte[], byte[]>(null, null);
        }

        try
        {
          if (fileStream.Position > fileStream.Length - 32)
          {
            Console.WriteLine("Not enough bytes in stream '{0}' to read uTXOHash (32 bytes)",
              fileStream.Length - fileStream.Position);
          }
          byte[] uTXOHash = await ReadBytesAsync(fileStream, 32);

          int uTXOLength = (int)VarInt.ParseVarInt(fileStream, out int lengthVarInt);
          fileStream.Position -= lengthVarInt;
          if (uTXOLength > 500)
          {
            Console.WriteLine("Bad VarInt '{0}' parsing", uTXOLength);
          }

          if (fileStream.Position > (fileStream.Length - lengthVarInt - uTXOLength))
          {
            Console.WriteLine("Not enough bytes in stream '{0}' to read uTXO ('{1}' bytes)",
              fileStream.Length - fileStream.Position,
              lengthVarInt + uTXOLength);
          }
          byte[] uTXOSerialized = await ReadBytesAsync(fileStream, lengthVarInt + uTXOLength);

          return new KeyValuePair<byte[], byte[]>(uTXOHash, uTXOSerialized);
        }
        catch (IndexOutOfRangeException)
        {
          Console.WriteLine("Corrupted UTXO file, should fix that.");
          return new KeyValuePair<byte[], byte[]>(null, null);
        }
        catch (ArgumentException)
        {
          return new KeyValuePair<byte[], byte[]>(null, null);
        }
      }

      static async Task<byte[]> ReadBytesAsync(FileStream fileStream, long bytesToRead)
      {
        var buffer = new byte[bytesToRead];
        int offset = 0;
        while (bytesToRead > 0)
        {
          int chunkSize = await fileStream.ReadAsync(buffer, offset, (int)bytesToRead);

          if(chunkSize == 0)
          {
            Console.WriteLine("chunksize == 0, bytes to read '{0}'", bytesToRead);
          }
          
          offset += chunkSize;
          bytesToRead -= chunkSize;
        }

        return buffer;
      }
    }
  }
}
