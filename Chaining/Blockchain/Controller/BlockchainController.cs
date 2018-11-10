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
    partial class BlockchainController
    {
      INetwork Network;
      Blockchain Blockchain;
      IHeaderArchiver Archiver;


      public BlockchainController(INetwork network, Blockchain blockchain, IHeaderArchiver archiver)
      {
        Network = network;
        Blockchain = blockchain;
        Archiver = archiver;
      }

      public void Start()
      {
        LoadHeadersFromArchive();
        Debug.WriteLine("blockchain height after archive load: '{0}'", Blockchain.MainChain.Height);

        Network.QueueSession(new SessionHeaderDownload(Blockchain, Archiver));

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
              Blockchain.InsertHeader(header);

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
            Blockchain.InsertHeader(header);
          }
          catch (BlockchainException ex)
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
        }
      }
          
    }
  }
}