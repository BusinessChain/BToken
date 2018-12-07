﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    Headerchain Headers;
    static IPayloadParser PayloadParser;
    INetwork Network;
    BlockArchiver Archiver;


    public Blockchain(
      NetworkBlock genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Network = network;
      Headers = new Headerchain(genesisBlock.Header, network, checkpoints, this);
      PayloadParser = payloadParser;

      Archiver = new BlockArchiver(this, network);
    }

    public async Task StartAsync()
    {
      await Headers.StartAsync();
    }

    public async Task InitialBlockDownloadAsync()
    {
      await Archiver.InitialBlockDownloadAsync();
    }

    public void DownloadBlock(NetworkHeader header)
    {

    }

  }
}
