using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    public class BlockchainPeer
    {
      Network.INetworkChannel NetworkChannel;

      const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;



      public BlockchainPeer(Network.INetworkChannel networkChannel)
      {
        NetworkChannel = networkChannel;
      }


      public async Task<Headerchain.HeaderContainer> GetHeaders(
        IEnumerable<byte[]> locatorHashes)
      {
        await NetworkChannel.SendMessage(
          new GetHeadersMessage(locatorHashes));

        int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
        CancellationTokenSource cancellation = new CancellationTokenSource(timeout);

        byte[] headerBytes = 
          (await NetworkChannel.ReceiveMessage(
            cancellation.Token,
            "headers")).Payload;

        var headerContainer = 
          new Headerchain.HeaderContainer(headerBytes);

        headerContainer.Parse();

        return headerContainer;
      }

      

      public async Task DownloadBlocks(DataBatch uTXOBatch)
      {
        if (uTXOBatch.DataContainers.Count == 0)
        {
          return;
        }

        List<byte[]> hashesRequested = new List<byte[]>();

        foreach (BlockContainer blockBatchContainer in
          uTXOBatch.DataContainers)
        {
          if (blockBatchContainer.Buffer == null)
          {
            hashesRequested.Add(
              blockBatchContainer.Header.HeaderHash);
          }
        }

        await NetworkChannel.SendMessage(
          new GetDataMessage(
            hashesRequested
            .Select(h => new Inventory(
              InventoryType.MSG_BLOCK, h))
              .ToList()));

        var cancellationDownloadBlocks =
          new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        foreach (BlockContainer blockBatchContainer in
          uTXOBatch.DataContainers)
        {
          if (blockBatchContainer.Buffer != null)
          {
            continue;
          }

          blockBatchContainer.Buffer =
            await ReceiveBlock(cancellationDownloadBlocks.Token)
            .ConfigureAwait(false);

          blockBatchContainer.Parse();
          uTXOBatch.CountItems += blockBatchContainer.CountItems;
        }
      }



      async Task<byte[]> ReceiveBlock(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage =
            await NetworkChannel
            .ReceiveApplicationMessage(cancellationToken)
            .ConfigureAwait(false);

          if (networkMessage.Command != "block")
          {
            continue;
          }

          return networkMessage.Payload;
        }
      }


      public void ReportDuplicate()
      {
        throw new NotImplementedException();
      }
      public void ReportInvalid()
      {
        NetworkChannel.ReportInvalid();
      }

      public string GetIdentification()
      {
        return NetworkChannel.GetIdentification();
      }


      public void Release()
      {
        NetworkChannel.Release();
      }

      public void Dispose()
      {
        NetworkChannel.Dispose();
      }
    }
  }
}
