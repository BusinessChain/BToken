using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                await InsertHeadersAsync(headersMessage.Headers);
                break;

              default:
                throw new NetworkException("Received improper session message.");
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
                  case BlockCode.ORPHAN:
                    State = SessionState.ORPHAN;
                    BlockchainSession.BlameProtocolError();
                    HeaderOrphan = header;
                    await BlockchainSession.GetHeadersAsync();
                    return;

                  case BlockCode.DUPLICATE:
                    State = SessionState.END;
                    return;

                  case BlockCode.INVALID:
                    BlockchainSession.BlameConsensusError();
                    throw ex;

                  default:
                    throw ex;
                }
              }
            }
            
            if (headers.Any())
            {
              await BlockchainSession.GetHeadersAsync();
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
