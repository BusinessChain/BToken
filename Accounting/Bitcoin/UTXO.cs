using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    INetwork Network;
    Blockchain Blockchain;


    // API
    public UTXO(Blockchain blockchain, INetwork network)
    {
      Network = network;
      Blockchain = blockchain;

    }


    public void Start()
    {
      // listen to Bitcoin messages
    }

  }
}
