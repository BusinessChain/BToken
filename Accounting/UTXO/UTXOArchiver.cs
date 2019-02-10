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
      Network Network;

      public UTXOArchiver(UTXO uTXO, Network network)
      {
        UTXO = uTXO;
        Network = network;
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
        string filename = hash.ToString();
        string fileRootPath = GenerateRootPath(hash);

        DirectoryInfo dir = Directory.CreateDirectory(fileRootPath);

        string filePath = Path.Combine(fileRootPath, filename);

        return new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None);
      }

      public async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
      {
        return await ReadBlockAsync(hash, null);
      }
      public async Task<NetworkBlock> ReadBlockAsync(UInt256 hash, INetworkChannel channel)
      {
        // read cache

        string filename = hash.ToString();
        string fileRootPath = GenerateRootPath(hash);
        string filePath = Path.Combine(fileRootPath, filename);
        
        try
        {
          using (FileStream fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
          {
            return await NetworkBlock.ReadBlockAsync(fileStream);
          }
        }
        catch (FileNotFoundException)
        {
          var sessionBlockDownload = new SessionBlockDownload(hash);

          if (channel == null || !await channel.TryExecuteSessionAsync(sessionBlockDownload, default(CancellationToken)))
          {
            await Network.ExecuteSessionAsync(sessionBlockDownload);
          }
          
          Task archiveBlockTask = ArchiveBlockAsync(sessionBlockDownload.Block, hash);
          return sessionBlockDownload.Block;
        }
      }

      static string GenerateRootPath(UInt256 blockHash)
      {
        string blockHashIndex = new SoapHexBinary(blockHash.GetBytes().Take(2).ToArray()).ToString();

        return Path.Combine(
          RootDirectory.Name,
          blockHashIndex);
      }
    }
  }
}