using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Headerchain
  {
    partial class HeaderchainController
    {
      class SessionHeaderDownload : INetworkSession
      {
        Headerchain Blockchain;
        IHeaderArchiver Archiver;

        INetworkChannel Channel;

        BufferBlock<bool> SignalSessionCompletion = new BufferBlock<bool>();


        public SessionHeaderDownload(Headerchain blockchain, IHeaderArchiver archiver)
        {
          Blockchain = blockchain;
          Archiver = archiver;
        }

        public async Task RunAsync(INetworkChannel channel)
        {
          Channel = channel;

          await DownloadHeadersAsync();

          SignalSessionCompletion.Post(true);
        }

        public async Task AwaitSignalCompletedAsync()
        {
          while(true)
          {
            bool signalCompleted = await SignalSessionCompletion.ReceiveAsync();

            if (signalCompleted) { return; }
          }
        }

        async Task DownloadHeadersAsync()
        {
          List<NetworkHeader> headers = await Channel.GetHeadersAsync(Blockchain.Locator.GetHeaderLocator());

          using (var archiveWriter = Archiver.GetWriter())
          {
            while (headers.Any())
            {
              InsertHeaders(headers, archiveWriter);

              headers = await Channel.GetHeadersAsync(Blockchain.Locator.GetHeaderLocator());
            }
          }
        }

        void InsertHeaders(List<NetworkHeader> headers, IHeaderWriter archiveWriter)
        {
          foreach (NetworkHeader header in headers)
          {
            try
            {
              Blockchain.InsertHeader(header);
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

            archiveWriter.StoreHeader(header);
          }
        }


      }
    }
  }
}
