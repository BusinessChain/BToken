using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
  {
    partial class UTXOSynchronizer : DataSynchronizer
    {
      class UTXOChannel
      {
        public Network.INetworkChannel NetworkChannel;



        public UTXOChannel(Network.INetworkChannel networkChannel)
        {
          NetworkChannel = networkChannel;
        }



        public async Task DownloadBlocks(DataBatch uTXOBatch)
        {
          if(uTXOBatch.DataContainers.Count == 0)
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

            blockBatchContainer.TryParse();
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



        public void Dispose()
        {
          NetworkChannel.Dispose();
        }

        public void Release()
        {
          NetworkChannel.Release();
        }
      }
    }
  }
}
