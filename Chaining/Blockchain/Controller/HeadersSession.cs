using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainController
    {
      partial class BlockchainSession
      {
        class HeadersSession
        {
          BlockchainSession BlockchainSession;

          enum SessionState { START, ORPHAN, END };
          SessionState State = SessionState.START;

          NetworkHeader HeaderOrphan;
          List<UInt256> BlockLocator;

                    
          public HeadersSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
          }

          public async Task StartAsync(HeadersMessage headersMessage)
          {
            await ProcessMessageAsync(headersMessage);

            while (State != SessionState.END)
            {
              NetworkMessage networkMessage = await BlockchainSession.GetNextNetworkMessageAsync();
              await ProcessMessageAsync(networkMessage);
            }
          }

          async Task ProcessMessageAsync(NetworkMessage networkMessage)
          {
            switch (networkMessage)
            {
              case HeadersMessage headersMessage:
                await ProcessHeadersMessageAsync(headersMessage);
                break;

              default:
                throw new NetworkException("Received improper session message.");
            }
          }
          async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage)
          {
            switch (State)
            {
              case SessionState.START:
                await InsertHeadersAsync(headersMessage.Headers);
                break;

              case SessionState.ORPHAN:
                await InsertHeadersAsync(headersMessage.Headers);
                break;
            }
          }
          async Task InsertHeadersAsync(List<NetworkHeader> headers)
          {
            foreach (NetworkHeader header in headers)
            {
              try
              {
                BlockchainSession.Controller.Blockchain.insertHeader(header);
              }
              catch (BlockchainException ex)
              {
                switch (ex.ErrorCode)
                {
                  case BlockCode.DUPLICATE:
                    State = SessionState.END;
                    return;

                  case BlockCode.ORPHAN:
                    State = SessionState.ORPHAN;
                    HeaderOrphan = header;
                    BlockLocator = BlockchainSession.GetBlockLocator();
                    await BlockchainSession.GetHeadersAsync(BlockLocator);
                    return;

                  case BlockCode.INVALID:
                    return;

                  default:
                    throw ex;
                }
              }
            }

            if (headers.Any())
            {
              await BlockchainSession.GetHeadersAsync(BlockchainSession.GetLastBlockHash());
            }
            else
            {
              State = SessionState.END;
            }
          }

        }
      }
    }
  }
}
