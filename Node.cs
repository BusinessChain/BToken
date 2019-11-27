using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using BToken.Chaining;
using BToken.Networking;

// Test

namespace BToken
{
  partial class Node
  {
    Network Network;
    UTXOTable UTXOTable;
    Headerchain Headerchain;

    Wallet Wallet;

    BitcoinGenesisBlock GenesisBlock = new BitcoinGenesisBlock();
    List<HeaderLocation> Checkpoints = new List<HeaderLocation>()
      {
        new HeaderLocation(height : 11111, hash : "0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"),
        new HeaderLocation(height : 250000, hash : "000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214"),
        new HeaderLocation(height : 535419, hash : "000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39")
      };

    public Node()
    {
      Network = new Network();

      Headerchain = new Headerchain(
        GenesisBlock.Header,
        Checkpoints);
      Headerchain.Network = Network;

      UTXOTable = new UTXOTable(
        GenesisBlock.BlockBytes,
        Headerchain);
      UTXOTable.Network = Network;


      Wallet = new Wallet();
    }

    public async Task StartAsync()
    {
      StartListener();

      Network.Start();

      await Headerchain.Start();

      await UTXOTable.Start();

      Wallet.GeneratePublicKey();
    }

    async Task StartListener()
    {
      while (true)
      {
        Network.INetworkChannel channel =
          await Network.AcceptChannelInboundRequestAsync();

        List<NetworkMessage> messages = channel.GetApplicationMessages();

        if (!UTXOTable.Synchronizer.GetIsSyncingCompleted())
        {
          channel.Release();
          continue;
        }
        foreach (NetworkMessage message in messages)
        {
          try
          {
            switch (message.Command)
            {
              case "getdata":
                var getDataMessage = new GetDataMessage(message);

                foreach (Inventory inventory in getDataMessage.Inventories)
                {
                  if (inventory.Type == InventoryType.MSG_BLOCK)
                  {
                    if (UTXOTable.Synchronizer.TryGetBlockFromArchive(
                      inventory.Hash,
                      out byte[] blockBytes))
                    {
                      NetworkMessage blockMessage = new NetworkMessage(
                        "block",
                        blockBytes);

                      await channel.SendMessage(blockMessage);
                    }
                    else
                    {
                      // Send reject message;
                    }
                  }
                }

                break;

              case "getheaders":
                var getHeadersMessage = new GetHeadersMessage(message);

                var headers = Headerchain.GetHeaders(
                  getHeadersMessage.HeaderLocator,
                  2000,
                  getHeadersMessage.StopHash);

                await channel.SendMessage(
                  new HeadersMessage(headers));

                break;

              case "inv":
                var invMessage = new InvMessage(message);

                foreach(Inventory inv in invMessage.Inventories
                  .Where(inv => inv.Type == InventoryType.MSG_BLOCK).ToList())
                {
                  Console.WriteLine("inv message {0} from {1}",
                       inv.Hash.ToHexString(),
                       channel.GetIdentification());

                  if (Headerchain.TryReadHeader(
                    inv.Hash, 
                    out Header headerAdvertized))
                  {
                    Console.WriteLine(
                      "Advertized block {0} already in chain",
                      inv.Hash.ToHexString());

                    break;
                  }

                  Headerchain.Synchronizer.LoadBatch();
                  await Headerchain.Synchronizer.DownloadHeaders(channel);

                  if (Headerchain.Synchronizer.TryInsertBatch())
                  {
                    if (!await UTXOTable.Synchronizer.TrySynchronize(channel))
                    {
                      Console.WriteLine(
                        "Could not synchronize UTXO, with channel {0}",
                        channel.GetIdentification());
                    }
                  }
                  else
                  {
                    Console.WriteLine(
                      "Failed to insert header message from channel {0}",
                      channel.GetIdentification());
                  }
                }
                
                break;

              case "headers":
                var headersMessage = new HeadersMessage(message);

                Console.WriteLine("headers message {0} from {1}",
                  headersMessage.Headers.First().HeaderHash.ToHexString(),
                  channel.GetIdentification());

                if (Headerchain.TryReadHeader(headersMessage.Headers.First().HeaderHash, out Header header))
                {
                  Console.WriteLine(
                    "Advertized block {0} already in chain",
                    headersMessage.Headers.First().HeaderHash.ToHexString());

                  break;
                }

                if (Headerchain.Synchronizer.TryInsertHeaderBytes(
                  headersMessage.Payload))
                {
                  headersMessage.Headers.ForEach(
                    h => Console.WriteLine("inserted header {0}",
                    h.HeaderHash.ToHexString()));

                  Console.WriteLine("blockheight {0}", Headerchain.GetHeight());

                  if (!await UTXOTable.Synchronizer.TrySynchronize(channel))
                  {
                    Console.WriteLine("Could not synchronize UTXO, with channel {0}",
                      channel.GetIdentification());
                  }
                }
                else
                {
                  Console.WriteLine("Failed to insert header message from channel {0}",
                    channel.GetIdentification());
                }

                break;

              default:
                break;
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "Serving inbound request {0} of channel {1} ended in exception {2}",
              message.Command,
              channel.GetIdentification(),
              ex.Message);

            Network.DisposeChannel(channel);
          }
        }

        channel.Release();

      }
    }
  }
}