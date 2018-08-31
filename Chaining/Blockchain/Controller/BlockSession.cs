using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainController
    {
      partial class BlockchainSession
      {
        class BlockSession
        {
          BlockchainSession BlockchainSession;

          List<List<BlockLocation>> GetBlockBatches;


          public BlockSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
            CreateGetBlockBatches();
          }
          void CreateGetBlockBatches()
          {
            ChainSocket socket = BlockchainSession.Controller.Blockchain.SocketMain;

          }

          public async Task StartAsync()
          {

          }
        }
      }
    }
  }
}
