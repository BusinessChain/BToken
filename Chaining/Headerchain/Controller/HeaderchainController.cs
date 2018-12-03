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
        LoadHeadersFromArchive();

        Task inboundSessionRequestListenerTask = StartInboundSessionRequestListenerAsync();

        await DownloadHeaderchainAsync();

        await Headerchain.Blockchain.InitialBlockDownloadAsync();
        
      }
      void LoadHeadersFromArchive()
      {
        try
        {
          using (var archiveReader = Archiver.GetReader())
          {
            NetworkHeader header = archiveReader.GetNextHeader();

            while (header != null)
            {
              Headerchain.InsertHeader(header);

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
        await Network.ExecuteSessionAsync(sessionHeaderDownload, default(CancellationToken));
      }

      async Task StartInboundSessionRequestListenerAsync()
      {
        while (true)
        {
          using (INetworkChannel channel = await Network.AcceptChannelInboundSessionRequestAsync())
          {
            List<NetworkMessage> sessionRequests = channel.GetRequestMessages();

            foreach (NetworkMessage networkMessage in sessionRequests)
            {
              INetworkSession session = null;

              switch (networkMessage.Command)
              {
                case "inv":
                  //await ProcessInventoryMessageAsync(invMessage);
                  break;

                case "getheaders":
                  //var getHeadersMessage = new get();
                  break;

                case "headers":
                  break;

                case "block":
                  break;

                default:
                  break;
              }
              
              var executeSessionTask = channel.TryExecuteSessionAsync(session, default(CancellationToken));
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
      void ProcessHeadersMessage(HeadersMessage headersMessage)
      {
        foreach (NetworkHeader header in headersMessage.Headers)
        {
          try
          {
            Headerchain.InsertHeader(header);
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
          
    }
  }
}