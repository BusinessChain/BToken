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


    public async Task InsertHeaders(
      byte[] headerBytes,
      Network.INetworkChannel channel)
    {
      var headerBatch = new DataBatch()
      {
        DataContainers = new List<DataContainer>()
          {
            new Headerchain.HeaderContainer(headerBytes)
          }
      };

      headerBatch.TryParse();

      if (!headerBatch.IsValid)
      {
        channel.ReportInvalid();
        return;
      }

      await LockChain();

      Header headerRoot = ((Headerchain.HeaderContainer)headerBatch
        .DataContainers.First()).HeaderRoot;

      if (Headerchain.TryReadHeader(
        headerRoot.HeaderHash, 
        out Header header))
      {
        if (UTXOTable
          .Synchronizer
          .MapBlockToArchiveIndex
          .ContainsKey(headerRoot.HeaderHash))
        {
          ReleaseChain();
          channel.ReportDuplicate();
          return;
        }
      }
      else
      {
        try
        {
          Headerchain.Synchronizer.InsertHeaderBatch(headerBatch);
          
          Console.WriteLine("inserted {0} headers with root {1} from {2}",
            headerBatch.CountItems,
            headerRoot.HeaderHash.ToHexString(),
            channel.GetIdentification());
        }
        catch (ChainException ex)
        {
          switch (ex.ErrorCode)
          {
            case ErrorCode.ORPHAN:
              // synchronize
              IEnumerable<byte[]> headerLocator = 
                Headerchain.Locator.GetHeaderHashes();

              byte[] headers = await channel.GetHeaders(headerLocator);
              break;

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
