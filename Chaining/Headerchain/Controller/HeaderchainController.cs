using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      partial class HeaderchainController
      {
        INetwork Network;
        Headerchain Headerchain;
        IHeaderArchiver Archiver;


        public HeaderchainController(INetwork network, Headerchain headerchain, IHeaderArchiver archiver)
        {
          Network = network;
          Headerchain = headerchain;
          Archiver = archiver;
        }

        public async Task StartAsync()
        {
          await LoadHeadersFromArchiveAsync();

          Task inboundSessionRequestListenerTask = StartInboundRequestListenerAsync();

          await DownloadHeaderchainAsync();

          await Headerchain.Blockchain.InitialBlockDownloadAsync();

        }
        async Task LoadHeadersFromArchiveAsync()
        {
          try
          {
            using (var archiveReader = Archiver.GetReader())
            {
              NetworkHeader header = archiveReader.GetNextHeader();

              while (header != null)
              {
                await Headerchain.InsertHeaderAsync(header);

                header = archiveReader.GetNextHeader();
              }
            }
          }
          catch (Exception ex)
          {
            Debug.WriteLine(ex.Message);
          }
        }
        async Task DownloadHeaderchainAsync()
        {
          var sessionHeaderDownload = new SessionHeaderDownload(Headerchain, Archiver);
          await Network.ExecuteSessionAsync(sessionHeaderDownload);
        }

        async Task StartInboundRequestListenerAsync()
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
                    ServeGetHeadersRequest(getHeadersMessage, channel);
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

            using (var archiveWriter = Archiver.GetWriter())
            {
              archiveWriter.StoreHeader(header);
            }

            Headerchain.Blockchain.DownloadBlock(header);
          }
        }
        void ServeGetHeadersRequest(GetHeadersMessage getHeadersMessage, INetworkChannel channel)
        {
          List<NetworkHeader> headers = new List<NetworkHeader>();

          var headerStreamer = new HeaderStreamer(MainChain);

          headerStreamer.FindRootLocation(getHeadersMessage.HeaderLocator);

          NetworkHeader header = headerStreamer.ReadNextHeader();
          while (header != null)
          {
            headers.Add(header);
            header = headerStreamer.ReadNextHeader();
          }

          var headersMessage = new HeadersMessage(headers);
          channel.SendMessageAsync(headersMessage);
        }
        HeaderStreamer GetHeaderStreamer(List<UInt256> headerLocator)
        {
          var probe = new ChainProbe(Headerchain.MainChain);

          while (true)
          {
            bool isProbeHashInLocator = headerLocator.Any(h => h.IsEqual(probe.Hash));
            if (isProbeHashInLocator)
            {
              return probe;
            }
            if (probe.Header == GenesisHeader)
            {
              return false;
            }

            Push();
          }
        }
        static void SetStreamerPositionToMutualRoot(HeaderStreamer headerStreamer, List<UInt256> headerLocator)
        {
          // Use probe instead of streamer?
          ChainLocation streamLocation = headerStreamer.ReadNextHeaderLocationTowardRoot();
          while (streamLocation != null)
          {
            if (headerLocator.Any(h => h.IsEqual(streamLocation.Hash)))
            {
              return;
            }

            streamLocation = headerStreamer.ReadNextHeaderLocationTowardRoot();
          }
        }


      }
    }
  }
}