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
    class UTXOChannel
    {
      public INetworkChannel NetworkChannel;
      public DataBatch Batch;



      public UTXOChannel(INetworkChannel networkChannel)
      {
        NetworkChannel = networkChannel;
      }



      public async Task RequestBlocks(IEnumerable<byte[]> headerHashes)
      {
        await NetworkChannel.SendMessage(
          new GetDataMessage(
            headerHashes.Select(h => new Inventory(InventoryType.MSG_BLOCK, h))));
      }



      public async Task<byte[]> ReceiveBlock(CancellationToken cancellationToken)
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

    }
  }
}
