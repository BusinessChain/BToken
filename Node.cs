using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  public partial class Node
  {
    Network Network;
    UTXOTable UTXO;
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
        Checkpoints,
        Network);

      UTXO = new UTXOTable(
        GenesisBlock.BlockBytes,
        Headerchain,
        Network);

      Wallet = new Wallet();
    }

    public async Task StartAsync()
    {
      StartListener();

      Network.Start();

      await Headerchain.Start();

      await UTXO.Start();
      
      Wallet.GeneratePublicKey();
    }



    
    async Task StartListener()
    {
      while (true)
      {
        INetworkChannel channel =
          await Network.AcceptChannelInboundRequestAsync();

        List<NetworkMessage> messages = channel.GetApplicationMessages();
        
        try
        {
          foreach (NetworkMessage message in messages)
          {
            switch (message.Command)
            {
              case "getheaders":
                //var getHeadersMessage = new GetHeadersMessage(inboundMessage);
                //var headers = Headerchain.GetHeaders(getHeadersMessage.HeaderLocator, getHeadersMessage.StopHash);
                //await channel.SendMessageAsync(new HeadersMessage(headers));
                break;

              case "inv":
                var invMessage = new InvMessage(message);

                if (invMessage.Inventories.First().Type.ToString() != "MSG_TX")
                {
                  Console.WriteLine("inv message with {0} {1} from channel {2}",
                    invMessage.Inventories.Count,
                    invMessage.Inventories.First().Type.ToString(),
                    channel.GetIdentification());
                }

                break;

              case "headers":
                var headersMessage = new HeadersMessage(message);

                Console.WriteLine("header message from channel {0}",
                  channel.GetIdentification());
                
                if (Headerchain.TryInsertBuffer(headersMessage.Payload))
                {
                  Console.WriteLine("Contact UTXO on received header");
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

          Network.ReturnChannel(channel);
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
