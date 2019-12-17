using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  class Blockchain
  {
    Headerchain Headerchain;
    UTXOTable UTXOTable;



    public Blockchain(
      Headerchain headerchain, 
      UTXOTable uTXOTable)
    {
      Headerchain = headerchain;
      UTXOTable = uTXOTable;
    }


    public async Task RunSyncSession(Network.INetworkChannel channel)
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
          Headerchain.InsertHeadersTentatively(
            headerContainer.HeaderRoot,
            stopHash);
        }
        catch (ChainException)
        {
          channel.ReportInvalid();
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

      if(Headerchain.IsTentativeChainStrongerThanMainchain())
      {
        Header header = Headerchain.HeaderRootTentative;

        UTXOTable.RollBackToHeader(header);
        
        while (header.HeadersNext.Any())
        {
          header = header.HeadersNext.Last();

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

          Headerchain.InsertHeadersTentatively(
            headerContainer.HeaderRoot,
            stopHash);
        }
        catch (ChainException ex)
        {
          switch (ex.ErrorCode)
          {
            case ErrorCode.ORPHAN:
              RunSyncSession(channel);
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
