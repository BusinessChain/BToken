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

        var sessionHeaderDownload = new SessionHeaderDownload(Headerchain, Archiver);
        Network.PostSession(sessionHeaderDownload);
        await sessionHeaderDownload.AwaitSignalCompletedAsync();

        await Headerchain.Blockchain.InitialBlockDownloadAsync();
        
        Task startMessageListenerTask = StartMessageListenerAsync();

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
      async Task StartMessageListenerAsync()
      {
        while (true)
        {
          NetworkMessage networkMessage = await Network.GetMessageBlockchainAsync();

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
      void ProcessHeadersMessage(Network.HeadersMessage headersMessage)
      {
        foreach (NetworkHeader header in headersMessage.Headers)
        {
          try
          {
            Headerchain.InsertHeader(header);
          }
          catch (HeaderchainException ex)
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

          Headerchain.Blockchain.DownloadBlock();
        }
      }
          
    }
  }
}