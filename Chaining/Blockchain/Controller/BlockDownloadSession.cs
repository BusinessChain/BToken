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
        class BlockDownloadSession
        {
          BlockchainSession BlockchainSession;

          List<List<BlockLocation>> GetDataBatches;


          public BlockDownloadSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
            CreateGetDataBatches();
          }
          void CreateGetDataBatches()
          {
            // Go through the entire chain to search all headers with BlockPayload=null
            // The socket can provide information whether every Block is downloaded.
          }

          public async Task StartAsync()
          {

          }
        }
      }
    }
  }
}
