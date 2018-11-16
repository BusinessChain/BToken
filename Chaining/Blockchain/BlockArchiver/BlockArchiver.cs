using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

using BToken.Networking;
using BToken.Chaining;

namespace BToken.Chaining
{
  public partial class Blockchain : IBlockchain
  {
    partial class BlockArchiver
    {
      INetwork Network;
      Blockchain Blockchain;

      readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      static string ShardHandle = "Shard";
      uint ShardCountMax = 8;

      const int ITEM_COUNT_PER_DIRECTORY = 0x4;
      static string DirectoryHandle = "Shelf";

      const int BLOCK_REGISTER_BYTESIZE_MAX = 0x40000;
      static string FileHandle = "BlockRegister";

      List<Task<SessionBlockDownload>> BlockDownloadTasks;
      const uint SHARD_WRITERS_COUNT_MAX = 8;


      public BlockArchiver(Blockchain blockchain, INetwork network)
      {
        Blockchain = blockchain;
        Network = network;

        BlockDownloadTasks = new List<Task<SessionBlockDownload>>();
      }


      static NetworkBlock ParseNetworkBlock(FileStream blockRegisterStream)
      {
        int prefixInt = blockRegisterStream.ReadByte();

        if (prefixInt == -1)
        {
          return null;
        }

        int blockLength = (int)VarInt.ParseVarInt((ulong)prefixInt, blockRegisterStream);
        byte[] blockBytes = new byte[blockLength];
        int i = blockRegisterStream.Read(blockBytes, 0, blockLength);

        return NetworkBlock.ParseBlock(blockBytes);
      }

      static FileStream OpenFile(UInt256 fileHash)
      {
        string filename = fileHash.ToString();

        string firstHexByte = filename.Substring(0, 2);
        string secondHexByte = filename.Substring(2, 2);
        string thirdHexByte = filename.Substring(4, 2);

        string filePath = Path.Combine(
          RootDirectory.Name,
          firstHexByte,
          secondHexByte,
          thirdHexByte,
          filename);

        return new FileStream(
          filePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          BLOCK_REGISTER_BYTESIZE_MAX);
      }

      static void IncrementFileID(ref FileID fileID)
      {
        if (fileID.FileIndex == ITEM_COUNT_PER_DIRECTORY - 1)
        {
          fileID.DirectoryIndex++;
          fileID.FileIndex = 0;
        }
        else
        {
          fileID.FileIndex++;
        }
      }

      public static FileWriter GetWriter()
      {
        try
        {
          throw new NotImplementedException();
          //return new FileWriter(ShardEnumerator++);
        }
        catch (Exception ex)
        {
          Debug.WriteLine("BlockArchiver::GetWriter: " + ex.Message);
          throw ex;
        }
      }

      public async Task InitialBlockDownloadAsync()
      {
        int batchSize = 50;
        var blockstreamer = new Blockstreamer();

        List<ChainLocation> headerLocations = blockstreamer.ReadHeaderLocations(batchSize);
        while(headerLocations.Any())
        {
          FileWriter shardWriter = await GetShardWriterAsync();
          PostBlockDownloadSession(shardWriter, headerLocations);

          headerLocations = blockstreamer.ReadHeaderLocations(batchSize);
        }
      }
      async Task<FileWriter> GetShardWriterAsync()
      {
        if (BlockDownloadTasks.Count < SHARD_WRITERS_COUNT_MAX)
        {
          return new FileWriter((uint)BlockDownloadTasks.Count + 1);
        }
        else
        {
          Task<SessionBlockDownload> blockDownloadTaskCompleted = await Task.WhenAny(BlockDownloadTasks);
          BlockDownloadTasks.Remove(blockDownloadTaskCompleted);
          SessionBlockDownload sessionBlockDownload = await blockDownloadTaskCompleted;

          return sessionBlockDownload.ShardWriter;
        }
      }
      void PostBlockDownloadSession(FileWriter fileWriter, List<ChainLocation> headerLocations)
      {
        var sessionBlockDownload = new SessionBlockDownload(fileWriter, headerLocations);
        Network.PostSession(sessionBlockDownload);
        BlockDownloadTasks.Add(sessionBlockDownload.AwaitSessionCompletedAsync());
      }
    }
  }
}