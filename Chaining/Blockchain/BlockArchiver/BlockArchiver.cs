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
      INetwork Network;
      Blockchain Blockchain;

      static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);
      
      List<Task> BlockDownloadTasks;
      const uint DOWNLOAD_TASK_COUNT_MAX = 8;
      

      public BlockArchiver(Blockchain blockchain, INetwork network)
      {
        Blockchain = blockchain;
        Network = network;

        BlockDownloadTasks = new List<Task>();
      }

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
        using (FileStream fileStream = CreateFile(hash))
        {
          byte[] headerBytes = block.Header.GetBytes();
          byte[] txCount = VarInt.GetBytes(block.TXCount).ToArray();

          await fileStream.WriteAsync(headerBytes, 0, headerBytes.Length);
          await fileStream.WriteAsync(txCount, 0, txCount.Length);
          await fileStream.WriteAsync(block.Payload, 0, block.Payload.Length);
        }
      }

      public static async Task<NetworkBlock> ReadBlockAsync(UInt256 hash)
      {
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


      public async Task InitialBlockDownloadAsync()
      {
        var headerStreamer = new Headerchain.HeaderStreamer(Blockchain.Headers);

        ChainLocation headerLocation = headerStreamer.ReadNextHeaderLocationTowardRoot();
        while (headerLocation != null)
        {
          await AwaitNextDownloadTask();
          PostBlockDownloadSession(headerLocation);

          headerLocation = headerStreamer.ReadNextHeaderLocationTowardRoot();
        }
      }
      async Task AwaitNextDownloadTask()
      {
        if (BlockDownloadTasks.Count < DOWNLOAD_TASK_COUNT_MAX)
        {
          return;
        }
        else
        {
          Task blockDownloadTaskCompleted = await Task.WhenAny(BlockDownloadTasks);
          BlockDownloadTasks.Remove(blockDownloadTaskCompleted);
        }
      }
      void PostBlockDownloadSession(ChainLocation headerLocation)
      {
        var sessionBlockDownload = new SessionBlockDownload(this, headerLocation);

        Task executeSessionTask = Network.ExecuteSessionAsync(sessionBlockDownload);
        BlockDownloadTasks.Add(executeSessionTask);
      }
      
    }
  }
}