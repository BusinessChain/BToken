﻿using System.Diagnostics;

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
      INetworkChannel Channel;

      UInt256 HeaderHash;
      public NetworkBlock Block { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(UInt256 hashHeader)
      {
        HeaderHash = hashHeader;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        await DownloadBlockAsync();
      }

      async Task<NetworkBlock> DownloadBlockAsync()
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
              UInt256 hashReceived = blockMessage.NetworkBlock.Header.ComputeHeaderHash();
              if (hashReceived.Equals(HeaderHash))
              {
                Block = blockMessage.NetworkBlock;
              }
              else
              {
                Console.WriteLine("Requested block '{0}' but received '{1}' on channel '{2}'", HeaderHash, hashReceived, Channel.GetIdentification());
              }
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
