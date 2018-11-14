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


      public BlockArchiver(Blockchain blockchain, INetwork network)
      {
        Blockchain = blockchain;
        Network = network;
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

      static FileStream OpenFile(FileID fileID)
      {
        string filePath = Path.Combine(
          RootDirectory.Name,
          ShardHandle + fileID.ShardIndex,
          DirectoryHandle + fileID.DirectoryIndex,
          FileHandle + fileID.FileIndex);

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
        var sessionBlockDownloadTasks = new List<Task>();
        var ShardWriters = new List<FileWriter>();

        int batchSize = 2000;
        var blockstreamer = new Blockstreamer();
        List<NetworkHeader> headers = CreateHeaderBatch(batchSize);
        foreach(NetworkHeader startHeader in startHeaders)
        {
          var sessionBlockDownload = new SessionBlockDownload(this, startHeader, batchSize);
          Network.PostSession(sessionBlockDownload);
          sessionBlockDownloadTasks.Add(sessionBlockDownload.AwaitSignalCompletedAsync());
        }

        await Task.WhenAll(sessionBlockDownloadTasks);
      }

      List<NetworkHeader> CreateHeaderBatch(int batchSize)
      {
        Headerchain.ChainHeader header = Blockchain.Headerchain.GetHeader(new UInt256("000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f"));
        var startBlocks = new List<Headerchain.ChainHeader>() { header };
        return startBlocks;
      }

      void ReportSessionBlockckDownloadCompleted(SessionBlockDownload session)
      {
        //Network.QueueSession(new SessionBlockDownload(this, session.Archiver, headerStart, headerStop));
      }
    }
  }
}