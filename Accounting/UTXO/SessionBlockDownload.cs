using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class SessionBlockDownload : INetworkSession
    {
      UTXO UTXO;
      public INetworkChannel Channel { get; private set; }
      UInt256 HeaderHash;
      public NetworkBlock Block { get; private set; }
      public List<TX> TXs { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 10;


      public SessionBlockDownload(UTXO uTXO, UInt256 hashHeader)
      {
        UTXO = uTXO;
        HeaderHash = hashHeader;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        TXs = await DownloadBlockTXsAsync();
      }

      async Task<List<TX>> DownloadBlockTXsAsync()
      {
        try
        {
          var inventory = new Inventory(InventoryType.MSG_BLOCK, HeaderHash);
          await Channel.SendMessageAsync(new GetDataMessage(new List<Inventory>() { inventory }));

          var CancellationGetBlock = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

          while (true)
          {
            NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetBlock.Token);

            if (networkMessage.Command == "block")
            {
              var blockMessage = new BlockMessage(networkMessage);
              Block = blockMessage.NetworkBlock;
              UTXO.ValidateBlock(Block, HeaderHash, out List<TX> tXs);
              return tXs;
            }
          }
        }
        catch (TaskCanceledException ex)
        {
          Console.WriteLine("Canceled download of block '{0}' from peer '{1}' due to timeout '{2}' seconds",
            HeaderHash,
            Channel.GetIdentification(),
            SECONDS_TIMEOUT_BLOCKDOWNLOAD);

          throw ex;
        }
      }

    }
  }
}
