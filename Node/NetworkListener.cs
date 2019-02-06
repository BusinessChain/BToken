using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken
{
  public partial class BitcoinNode
  {
    class NetworkListener
    {
      Network Network;
      BitcoinNode BitcoinNode;


      public NetworkListener(Network network, BitcoinNode bitcoinNode)
      {
        Network = network;
        BitcoinNode = bitcoinNode;
      }

      public async Task StartAsync()
      {
        while (true)
        {
          using (INetworkChannel channel = await Network.AcceptChannelInboundRequestAsync().ConfigureAwait(false))
          {
            List<NetworkMessage> inboundMessages = channel.GetInboundRequestMessages();

            foreach (NetworkMessage inboundMessage in inboundMessages)
            {
              switch (inboundMessage.Command)
              {
                case "inv":
                  //await ProcessInventoryMessageAsync(invMessage);
                  break;

                case "getheaders":
                  var getHeadersMessage = new GetHeadersMessage(inboundMessage);
                  var headers = BitcoinNode.Headerchain.GetHeaders(getHeadersMessage.HeaderLocator, getHeadersMessage.StopHash);
                  await channel.SendMessageAsync(new HeadersMessage(headers));
                  break;

                case "headers":
                  var headersMessage = new HeadersMessage(inboundMessage);
                  await BitcoinNode.Headerchain.InsertHeadersAsync(headersMessage.Headers);
                  break;

                case "block":
                  break;

                default:
                  break;
              }
            }
          }
        }
      }
    }
  }
}
