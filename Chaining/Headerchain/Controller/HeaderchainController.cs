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

        await DownloadHeaderchainAsync();

        await Headerchain.Blockchain.InitialBlockDownloadAsync();
        
        Task inboundSessionRequestListenerTask = StartInboundSessionRequestListenerAsync();

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
          INetworkChannel channel = await Network.AcceptChannelInboundSessionRequestAsync();
          List<NetworkMessage> sessionRequests = channel.GetRequestMessages();

          foreach(NetworkMessage networkMessage in sessionRequests)
          {
            switch (networkMessage)
            {
              case InvMessage invMessage:
                //await ProcessInventoryMessageAsync(invMessage);
                break;

              case Network.HeadersMessage headersMessage:
                ProcessHeadersMessage(headersMessage);
                break;

              case Network.BlockMessage blockMessage:
                break;

              default:
                break;
            }
          }
        }
      }
      void ProcessHeadersMessage(Network.HeadersMessage headersMessage)
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