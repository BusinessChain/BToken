using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class GatewayUTXO : IGateway
    {
      Blockchain Blockchain;
      Network Network;
      UTXOTable UTXOTable;



      public GatewayUTXO(
        Blockchain blockchain,
        Network network,
        UTXOTable uTXOTable)
      {
        Blockchain = blockchain;
        Network = network;
        UTXOTable = uTXOTable;
      }



      public async Task Synchronize()
      {
      }



      public void ReportInvalidBatch(DataBatch batch)
      {
        throw new NotImplementedException();
      }
    }
  }
}
