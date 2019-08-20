using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    List<HeaderLocation> Checkpoints;
    UTXOTable UTXO;
    Headerchain Chain;
    Network Network;
    ArchiveBlockLoader ArchiveLoader;
    BlockchainNetworkGateway NetworkGateway;
    BitcoinGenesisBlock GenesisBlock;
    
    const int HASH_BYTE_SIZE = 32;
    const int COUNT_HEADER_BYTES = 80;
    const int COUNT_TXS_IN_BATCH_FILE = 50000;
        

    public Blockchain(
      BitcoinGenesisBlock genesisBlock,
      List<HeaderLocation> checkpoints, 
      Network network)
    {
      Checkpoints = checkpoints;
      Network = network;
      GenesisBlock = genesisBlock;

      Chain = new Headerchain(genesisBlock.Header, checkpoints, network);

      UTXO = new UTXOTable(this);
      ArchiveLoader = new ArchiveBlockLoader(this);
      NetworkGateway = new BlockchainNetworkGateway(this);
    }

    public async Task StartAsync()
    {
      await LoadAsync();

      NetworkGateway.Start();
    }
    async Task LoadAsync()
    {
      Task loadChainTask = Chain.LoadAsync();
      Task loadUTXOTask = UTXO.LoadAsync();

      await Task.WhenAll(new Task[] { loadChainTask, loadUTXOTask });
    }
  }
}