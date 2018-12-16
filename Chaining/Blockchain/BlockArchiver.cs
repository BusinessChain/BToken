﻿using System.Diagnostics;

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
      IPayloadParser PayloadParser;

      static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);
      
      List<Task> BlockDownloadTasks;
      const uint DOWNLOAD_TASK_COUNT_MAX = 8;
      

      public BlockArchiver(IPayloadParser payloadParser, Blockchain blockchain, INetwork network)
      {
        Blockchain = blockchain;
        PayloadParser = payloadParser;
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
        ValidateBlock(hash, block);

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
        using (FileStream blockFileStream = OpenFile(hash.ToString()))
        {
          byte[] blockBytes = new byte[blockFileStream.Length];
          int i = await blockFileStream.ReadAsync(blockBytes, 0, (int)blockFileStream.Length);

          var block = NetworkBlock.ParseBlock(blockBytes);

          ValidateBlock(hash, block);

          return block;
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


      public async Task InitialBlockDownloadAsync(Headerchain.HeaderStream headerStreamer)
      {
        ChainLocation headerLocation = headerStreamer.ReadHeaderLocationTowardGenesis();
        while (headerLocation != null)
        {
          if (!await TryValidateBlockExistingAsync(headerLocation.Hash))
          {
            await AwaitNextDownloadTask();
            PostBlockDownloadSession(headerLocation);
          }

          headerLocation = headerStreamer.ReadHeaderLocationTowardGenesis();
        }

        Console.WriteLine("Synchronizing blocks with network completed.");
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
        var sessionBlockDownload = new SessionBlockDownload(headerLocation, this);

        Task executeSessionTask = Network.ExecuteSessionAsync(sessionBlockDownload);
        BlockDownloadTasks.Add(executeSessionTask);
      }
      async Task<bool> TryValidateBlockExistingAsync(UInt256 hash)
      {
        try
        {
          NetworkBlock block = await ReadBlockAsync(hash);
          if (block == null)
          {
            return false;
          }

          return TryValidate(hash, block);
        }
        catch (IOException)
        {
          return false;
        }
      }
      bool TryValidate(UInt256 hash, NetworkBlock block)
      {
        try
        {
          ValidateBlock(hash, block);
          return true;
        }
        catch (ChainException)
        {
          return false;
        }
      }
      void ValidateBlock(UInt256 hash, NetworkBlock block)
      {
        UInt256 headerHash = block.Header.GetHeaderHash();
        if (!hash.IsEqual(headerHash))
        {
          throw new ChainException(HeaderCode.INVALID);
        }

        UInt256 payloadHash = PayloadParser.GetPayloadHash(block.Payload);
        if (!payloadHash.IsEqual(block.Header.MerkleRoot))
        {
          throw new ChainException(HeaderCode.INVALID);
        }
      }

    }
  }
}