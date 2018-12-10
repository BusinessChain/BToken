using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  class BlockchainRequestListener
  {
    INetwork Network;


    public BlockchainRequestListener(INetwork network)
    {
      Network = network;
    }

    public async Task StartAsync()
    {
      while (true)
      {
        using (INetworkChannel channel = await Network.AcceptChannelInboundRequestAsync())
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
                //ServeGetHeadersRequest(getHeadersMessage, channel);
                break;

              case "headers":
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
    async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage)
    {
      foreach (NetworkHeader header in headersMessage.Headers)
      {
        try
        {
          await Headerchain.InsertHeaderAsync(header);
        }
        catch (ChainException ex)
        {
          switch (ex.ErrorCode)
          {
            case BlockCode.ORPHAN:
              //await ProcessOrphanSessionAsync(headerHash);
              return;

            case BlockCode.DUPLICATE:
              return;

            default:
              throw ex;
          }
        }

        //using (var archiveWriter = Archiver.GetWriter())
        //{
        //  archiveWriter.StoreHeader(header);
        //}

        Blockchain.DownloadBlock(header);
      }
    }
    void ServeGetHeadersRequest(GetHeadersMessage getHeadersMessage, INetworkChannel channel)
    {
      Headerchain.HeaderStreamer headerStreamer = Headerchain.GetHeaderStreamer();
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
