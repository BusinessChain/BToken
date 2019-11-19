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

        try
        {
          foreach (NetworkMessage message in messages)
          {
            Console.WriteLine("{0} message from {1}",
              message.Command,
              channel.GetIdentification());

            switch (message.Command)
            {
              case "getheaders":
                var getHeadersMessage = new GetHeadersMessage(message);

                var headers = Headerchain.GetHeaders(
                  getHeadersMessage.HeaderLocator,
                  2000);

                await channel.SendMessage(
                  new HeadersMessage(headers));

                break;

              case "inv":
                var invMessage = new InvMessage(message);

                if (invMessage.Inventories.Any(
                  inv => inv.Type.ToString() == "MSG_BLOCK"))
                {
                  Console.WriteLine("block inventory message from channel {0}",
                    channel.GetIdentification());

                  Headerchain.Synchronizer.LoadBatch();
                  Headerchain.Synchronizer.DownloadHeaders(channel);

                  if (Headerchain.Synchronizer.TryInsertBatch())
                  {
                    if (!await UTXOTable.Synchronizer.TrySynchronize(channel))
                    {
                      Console.WriteLine("Could not synchronize UTXO, with channel {0}",
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

                if (Headerchain.Synchronizer.TryInsertHeaderBytes(
                  headersMessage.Payload))
                {
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

          channel.Release();
        }
        catch (Exception ex)
        {
          Console.WriteLine("Serving inbound request of channel '{0}' ended in exception '{1}'",
            channel.GetIdentification(),
            ex.Message);

          Network.DisposeChannel(channel);
        }

      }
    }
  }
}