﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;

using BToken.Networking;



namespace BToken.Chaining
{
  class Blockchain
  {
    Headerchain Headerchain;
    DataArchiver HeaderArchive;


    UTXOTable UTXOTable;
    DataArchiver UTXOArchive;

    Network Network;



    public Blockchain(
      Header genesisHeader,
      byte[] genesisBlockBytes,
      List<HeaderLocation> checkpoints,
      Network network)
    {
      Headerchain = new Headerchain(
        genesisHeader,
        checkpoints);

      HeaderArchive = new DataArchiver(
        Headerchain,
        Path.Combine(
          AppDomain.CurrentDomain.BaseDirectory,
          "HeaderArchive"),
        50000,
        4);


      UTXOTable = new UTXOTable(
        genesisBlockBytes,
        Headerchain);

      UTXOArchive = new DataArchiver(
        UTXOTable,
        "J:\\BlockArchivePartitioned",
        50000,
        4);

      Network = network;
    }



    public async Task Start()
    {
      await HeaderArchive.Load();
      await SynchronizeHeaderchain();

      UTXOTable.LoadImage();
      await SynchronizeUTXO();
    }


    async Task SynchronizeHeaderchain()
    {
      while (true)
      {
        Network.INetworkChannel channel =
          await Network.DispatchChannelOutbound()
          .ConfigureAwait(false);

        List<byte[]> headerLocator;

        lock (Headerchain.LOCK_IsChainLocked)
        {
          headerLocator =
            Headerchain.Locator.GetHeaderHashes().ToList();
        }

        try
        {
          var headerContainer = new Headerchain.HeaderContainer(
           await channel.GetHeaders(headerLocator));

          headerContainer.Parse();

          if (headerContainer.CountItems == 0)
          {
            return;
          }

          int indexLocatorRoot = headerLocator.FindIndex(
            h => h.IsEqual(headerContainer.HeaderRoot.HashPrevious));

          if (indexLocatorRoot == -1)
          {
            channel.ReportInvalid();
            return;
          }

          byte[] stopHash;
          if (indexLocatorRoot == headerLocator.Count - 1)
          {
            stopHash =
             ("00000000000000000000000000000000" +
             "00000000000000000000000000000000").ToBinary();
          }
          else
          {
            stopHash = headerLocator[indexLocatorRoot + 1];
          }


          while (
            Headerchain.TryReadHeader(
              headerContainer.HeaderRoot.HeaderHash,
              out Header header))
          {
            if(stopHash.IsEqual(headerContainer.HeaderRoot.HeaderHash))
            {
              channel.ReportInvalid();
              return;
            }

            if (headerContainer.HeaderRoot.HeaderNext != null)
            {
              headerContainer.HeaderRoot =
                headerContainer.HeaderRoot.HeaderNext;
            }
            else
            {
              headerLocator = new List<byte[]> {
                headerContainer.HeaderTip.HeaderHash };

              headerContainer = new Headerchain.HeaderContainer(
                await channel.GetHeaders(headerLocator));

              headerContainer.Parse();
              
              if (headerContainer.CountItems == 0)
              {
                channel.ReportDuplicate();
                // send tip to channel
                return;
              }

              indexLocatorRoot = headerLocator.FindIndex(
                h => h.IsEqual(headerContainer.HeaderRoot.HashPrevious));

              if (indexLocatorRoot == -1)
              {
                channel.ReportInvalid();
                return;
              }
            }
          }

          while (true)
          {
            if (stopHash.IsEqual(headerContainer.HeaderRoot.HeaderHash))
            {
              channel.ReportInvalid();
              return;
            }

            Headerchain.InsertHeaderBranchTentative(
              headerContainer.HeaderRoot);

            headerLocator = new List<byte[]> {
                headerContainer.HeaderTip.HeaderHash };

            headerContainer = new Headerchain.HeaderContainer(
              await channel.GetHeaders(headerLocator));

            headerContainer.Parse();

            if (headerContainer.CountItems == 0)
            {
              return;
            }

            indexLocatorRoot = headerLocator.FindIndex(
              h => h.IsEqual(headerContainer.HeaderRoot.HashPrevious));

            if (indexLocatorRoot == -1)
            {
              channel.ReportInvalid();
              return;
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(
            "{0} in SyncHeaderchainSession {1} with channel {2}: '{3}'",
            ex.GetType().Name,
            GetHashCode(),
            channel == null ? "'null'" : channel.GetIdentification(),
            ex.Message);

          channel.Dispose();
        }
      }
    }


    const int COUNT_UTXO_SESSIONS = 4;
    async Task SynchronizeUTXO()
    {
      if (UTXOTable.BlockHeight > Headerchain.HeightRootTentatively)
      {
        UTXOTable.RollBackToHeader(Headerchain.HeaderRootTentativeFork);
      }
      
      for (int i = 0; i < COUNT_UTXO_SESSIONS; i += 1)
      {
        RunUTXOSyncSession();
      }
    }



    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;

    async Task RunUTXOSyncSession()
    {
      Stopwatch stopwatchDownload = new Stopwatch();
      int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      DataBatch uTXOBatch = null;

      while (true)
      {
        UTXOChannel channel = new UTXOChannel(
          await Network.DispatchChannelOutbound());

        try
        {
          do
          {
            uTXOBatch = LoadBatch(countBlocks);

            stopwatchDownload.Restart();

            await channel.DownloadBlocks(uTXOBatch);

            stopwatchDownload.Stop();

            //await BatchSynchronizationBuffer.SendAsync(uTXOBatch);
            await UTXOTable.SendBatch(uTXOBatch);

            CalculateNewCountBlocks(
              ref countBlocks,
              stopwatchDownload.ElapsedMilliseconds);

          } while (!uTXOBatch.IsCancellationBatch);

          channel.Release();

          return;
        }
        catch (Exception ex)
        {
          Console.WriteLine("Exception {0} in block download: \n{1}" +
            "batch {2} queued",
            ex.GetType().Name,
            ex.Message,
            uTXOBatch.Index);

          QueueBatchesCanceled.Enqueue(uTXOBatch);

          channel.Dispose();

          countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        }
      }
    }




    ConcurrentQueue<DataBatch> QueueBatchesCanceled
      = new ConcurrentQueue<DataBatch>();
    static readonly object LOCK_LoadBatch = new object();
    int IndexLoad;
    Header HeaderLoad;

    DataBatch LoadBatch(int countHeaders)
    {
      if (QueueBatchesCanceled.TryDequeue(out DataBatch uTXOBatch))
      {
        return uTXOBatch;
      }

      lock (LOCK_LoadBatch)
      {
        uTXOBatch = new DataBatch(IndexLoad++);

        if (HeaderLoad == null)
        {
          HeaderLoad = UTXOTable.Header;
        }

        for (int i = 0; i < countHeaders; i += 1)
        {
          if (HeaderLoad.HeaderNext == null)
          {
            uTXOBatch.IsCancellationBatch = (i == 0);
            return uTXOBatch;
          }

          HeaderLoad = HeaderLoad.HeaderNext;

          BlockContainer blockContainer =
            new BlockContainer(
              UTXOTable.Headerchain,
              HeaderLoad);

          uTXOBatch.DataContainers.Add(blockContainer);
        }
      }

      return uTXOBatch;
    }





    async Task SynchronizeBlockchain(
      Network.INetworkChannel channel)
    {
      List<byte[]> headerLocator;

      lock (Headerchain.LOCK_IsChainLocked)
      {
        headerLocator = 
          Headerchain.Locator.GetHeaderHashes().ToList();
      }

      var headerContainer = new Headerchain.HeaderContainer(
        await channel.GetHeaders(headerLocator));

      headerContainer.Parse();

      if (headerContainer.CountItems == 0)
      {
        return;
      }
      
      int indexLocatorRoot = headerLocator.FindIndex(
        h => h.IsEqual(headerContainer.HeaderRoot.HashPrevious));
      
      if (indexLocatorRoot == -1)
      {
        channel.ReportInvalid();
        return;
      }
      
      byte[] stopHash;
      if (indexLocatorRoot == headerLocator.Count - 1)
      {
        stopHash =
         ("00000000000000000000000000000000" +
         "00000000000000000000000000000000").ToBinary();
      }
      else
      {
        stopHash = headerLocator[indexLocatorRoot + 1];
      }

      while (true)
      {                
        try
        {
          Headerchain.InsertHeaderBranchTentative(
            headerContainer.HeaderRoot,
            stopHash);
        }
        catch (ChainException ex)
        {
          channel.ReportInvalid();
          throw ex;
        }
        
        headerContainer = new Headerchain.HeaderContainer(
          await channel.GetHeaders(
            new List<byte[]> { headerContainer.HeaderTip.HeaderHash }));

        headerContainer.Parse();

        if (headerContainer.CountItems == 0)
        {
          break;
        }
      }

      if(Headerchain.IsBranchTentativeStrongerThanMain())
      {
        Header header = Headerchain.HeaderRootTentativeFork;

        UTXOTable.RollBackToHeader(header);
        
        while (header.HeaderNext != null)
        {
          header = header.HeaderNext;

          channel.RequestBlocks(
            new List<byte[]> { header.HeaderHash });
          
          var blockContainer = new UTXOTable.BlockContainer(
            Headerchain,
            header);

          blockContainer.Buffer = await channel.ReceiveBlock();

          blockContainer.Parse();

          UTXOTable.InsertContainer(blockContainer);
        }

        Headerchain.ReorgTentativeToMainChain();
      }
      else
      {
        Headerchain.DismissTentativeChain();
      }
      
    }



    public async Task InsertHeaders(
      byte[] headerBytes,
      Network.INetworkChannel channel)
    {
      var headerContainer = 
        new Headerchain.HeaderContainer(headerBytes);

      headerContainer.Parse();

      var headerBatch = new DataBatch();

      headerBatch.DataContainers.Add(headerContainer);
      
      await LockChain();

      if (Headerchain.TryReadHeader(
        headerContainer.HeaderRoot.HeaderHash, 
        out Header header))
      {
        if (UTXOTable
          .Synchronizer
          .MapBlockToArchiveIndex
          .ContainsKey(headerContainer.HeaderRoot.HeaderHash))
        {
          ReleaseChain();
          channel.ReportDuplicate();
          return;
        }
        else
        {
          // block runterladen
        }
      }
      else
      {
        try
        {
          byte[] stopHash =
           ("00000000000000000000000000000000" +
           "00000000000000000000000000000000").ToBinary();

          Headerchain.InsertHeaderBranchTentative(
            headerContainer.HeaderRoot,
            stopHash);
        }
        catch (ChainException ex)
        {
          switch (ex.ErrorCode)
          {
            case ErrorCode.ORPHAN:
              SynchronizeBlockchain(channel);
              return;

            case ErrorCode.INVALID:
              ReleaseChain();
              channel.ReportInvalid();
              return;
          }
        }
      }
      
      DataBatch blockBatch = 
        await channel.DownloadBlocks(headerBatch);

      try
      {
        UTXOTable.Synchronizer.InsertBatch(blockBatch);
      }
      catch (ChainException ex)
      {
        switch (ex.ErrorCode)
        {
          case ErrorCode.ORPHAN:
            // Block is not in Main chain
            break;

          case ErrorCode.INVALID:
            // Roll back inserted blocks
            // Restore UTXO by going to header tip 
            // (if there was no fork this doesn't do anything)
            ReleaseChain();
            channel.ReportInvalid();
            return;
        }
      }

      ReleaseChain();
    }



    public readonly object LOCK_IsChainLocked = new object();
    bool IsChainLocked;

    async Task LockChain()
    {
      while (true)
      {
        lock (LOCK_IsChainLocked)
        {
          if (!IsChainLocked)
          {
            IsChainLocked = true;
            return;
          }
        }

        await Task.Delay(100);
      }
    }

    void ReleaseChain()
    {
      lock (LOCK_IsChainLocked)
      {
        if (IsChainLocked)
        {
          IsChainLocked = false;
        }
      }
    }
  }
}
