using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class BlockchainRequestListener
    {
      INetwork Network;
      Blockchain Blockchain;


      public BlockchainRequestListener(Blockchain blockchain, INetwork network)
      {
        Network = network;
        Blockchain = blockchain;
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
                  ServeGetHeadersRequest(getHeadersMessage, channel);
                  break;

                case "headers":
                  var headersMessage = new HeadersMessage(inboundMessage);
                  await ProcessHeadersMessageAsync(headersMessage, channel);
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
      //async Task ProcessInventoryMessageAsync(NetworkMessage networkMessage)
      //{
      //  InvMessage invMessage = new InvMessage(networkMessage);

      //  if (invMessage.GetBlockInventories().Any()) // direkt als property zu kreationszeit anlegen.
      //  {
      //    await Network.NetworkMessageBufferBlockchain.SendAsync(invMessage).ConfigureAwait(false);
      //  }
      //  if (invMessage.GetTXInventories().Any())
      //  {
      //    await Network.NetworkMessageBufferUTXO.SendAsync(invMessage).ConfigureAwait(false);
      //  };
      //}
      async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage, INetworkChannel channel)
      {
        using (Headerchain.HeaderInserter headerInserter = Blockchain.Headers.GetHeaderInserter())
        {
          foreach (NetworkHeader header in headersMessage.Headers)
          {
            try
            {
              await headerInserter.InsertHeaderAsync(header);
            }
            catch (ChainException ex)
            {
              switch (ex.ErrorCode)
              {
                case HeaderCode.ORPHAN:
                  //await ProcessOrphanSessionAsync(headerHash);
                  return;

                case HeaderCode.DUPLICATE:
                  return;

                default:
                  throw ex;
              }
            }
          }
        }
      }
      void ServeGetHeadersRequest(GetHeadersMessage getHeadersMessage, INetworkChannel channel)
      {
        Headerchain.HeaderStream headerStreamer = Blockchain.Headers.GetHeaderStreamer();
        headerStreamer.FindRootLocation(getHeadersMessage.HeaderLocator);

        const int HEADERS_COUNT_MAX = 2000;
        var headers = new List<NetworkHeader>();
        UInt256 stopHash = getHeadersMessage.StopHash;
        NetworkHeader header = headerStreamer.ReadNextHeaderTowardTip(out UInt256 headerHash);

        while (header != null && headers.Count < HEADERS_COUNT_MAX && !(headerHash.IsEqual(stopHash)))
        {
          headers.Add(header);

          header = headerStreamer.ReadNextHeaderTowardTip(out headerHash);
        }

        var headersMessage = new HeadersMessage(headers);
        channel.SendMessageAsync(headersMessage);
      }

    }
  }
}
