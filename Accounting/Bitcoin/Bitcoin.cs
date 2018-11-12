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
    Headerchain Headerchain;
    BlockArchiver Archiver;


    // API
    public Bitcoin(Headerchain headerchain, INetwork network)
    {
      Network = network;
      Headerchain = headerchain;
      Archiver = new BlockArchiver(headerchain, network);

    }


    public void Start()
    {
      Archiver.InitialBlockDownload();
      // listen to Bitcoin messages
    }

  }
}
