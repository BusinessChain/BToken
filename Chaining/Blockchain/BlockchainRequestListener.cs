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
      Network Network;
      Blockchain Blockchain;

      const int HEADERS_COUNT_MAX = 2000;


      public BlockchainRequestListener(Blockchain blockchain, Network network)
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
        using (Headerchain.HeaderWriter headerInserter = Blockchain.Headers.GetHeaderInserter())
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
                case ChainCode.ORPHAN:
                  //await ProcessOrphanSessionAsync(headerHash);
                  return;

                case ChainCode.DUPLICATE:
                  return;

                default:
                  throw ex;
              }
            }
          }
        }
      }
      void ServeGetHeadersRequest(GetHeadersMessage messageGetHeaders, INetworkChannel channel)
      {
        Headerchain.HeaderReader headerStreamer = Blockchain.Headers.GetHeaderReader();
        var headers = new List<NetworkHeader>();
        
        NetworkHeader header = headerStreamer.ReadHeader(out ChainLocation headerLocation);
        while(header != null 
          && headers.Count < HEADERS_COUNT_MAX 
          && !messageGetHeaders.HeaderLocator.Contains(headerLocation.Hash))
        {
          if(headerLocation.Hash.Equals(messageGetHeaders.StopHash))
          {
            headers.Clear();
          }

          headers.Insert(0, header);
          header = headerStreamer.ReadHeader(out headerLocation);
        }

        channel.SendMessageAsync(new HeadersMessage(headers));
      }
    }
  }
}
