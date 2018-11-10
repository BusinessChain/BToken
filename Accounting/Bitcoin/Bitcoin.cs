using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class Bitcoin
  {
    INetwork Network;
    Blockchain Blockchain;
    BlockArchiver Archiver = new BlockArchiver();


    // API
    public Bitcoin(Blockchain blockchain, INetwork network)
    {
      Network = network;
      Blockchain = blockchain;
    }


    public void Start()
    {
      Network.QueueSession(new SessionBlockDownload(this, Archiver));
    }

  }
}
