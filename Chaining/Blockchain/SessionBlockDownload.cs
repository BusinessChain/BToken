using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class SessionBlockDownload : INetworkSession
    {
      INetworkChannel Channel;
      Blockchain Blockchain;

      ChainLocation HeaderLocation;

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(ChainLocation headerLocation, Blockchain blockchain)
      {
        HeaderLocation = headerLocation;
        Blockchain = blockchain;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        await DownloadBlockAsync();

        Console.WriteLine("Channel '{0}' downloaded block height: '{1}'",
          Channel.GetIdentification(),
          HeaderLocation.Height);
      }
      
      async Task DownloadBlockAsync()
      {
        NetworkBlock block = await GetBlockAsync(HeaderLocation.Hash);
        Blockchain.ValidateHeader(HeaderLocation.Hash, block);
        await Blockchain.Archiver.ArchiveBlockAsync(block, HeaderLocation.Hash);
      }
      async Task<NetworkBlock> GetBlockAsync(UInt256 hashRequested)
      {
        try
        {
          var inventory = new Inventory(InventoryType.MSG_BLOCK, hashRequested);
          await Channel.SendMessageAsync(new GetDataMessage(new List<Inventory>() { inventory }));

          var CancellationGetBlock = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

          while (true)
          {
            NetworkMessage networkMessage = await Channel.ReceiveMessageAsync(CancellationGetBlock.Token);

            if (networkMessage.Command == "block")
            {
              var blockMessage = new BlockMessage(networkMessage);
              UInt256 hashReceived = blockMessage.NetworkBlock.Header.ComputeHeaderHash();
              if (hashReceived.IsEqual(hashRequested))
              {
                return blockMessage.NetworkBlock;
              }
              else
              {
                Console.WriteLine("Requested block '{0}' but received '{1}' on channel '{2}'", hashRequested, hashReceived, Channel.GetIdentification());
              }
            }
          }
        }
        catch (TaskCanceledException ex)
        {
          Console.WriteLine("Canceled download of block '{0}' from peer '{1}' due to timeout '{2}' seconds",
            hashRequested,
            Channel.GetIdentification(),
            SECONDS_TIMEOUT_BLOCKDOWNLOAD);

          throw ex;
        }
      }

    }
  }
}
