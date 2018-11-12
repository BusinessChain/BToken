﻿using System;
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
    BlockArchiver Archiver;


    // API
    public UTXO(Blockchain blockchain, INetwork network)
    {
      Network = network;
      Blockchain = blockchain;
      Archiver = new BlockArchiver(blockchain, network);

    }


    public void Start()
    {
      Archiver.InitialBlockDownload();
      // listen to Bitcoin messages
    }

  }
}