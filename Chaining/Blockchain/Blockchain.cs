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
    Network Network;
    List<HeaderLocation> Checkpoints;

    BatchDataPipe UTXODataPipe;
    UTXOTable UTXO;
    readonly object LOCK_Chain = new object();

    BatchDataPipe HeaderchainDataPipe;
    Headerchain Chain;
    ArchiveBlockLoader ArchiveLoader;
    GatewayBlockchainNetwork NetworkGateway;
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
      GenesisBlock = genesisBlock;
      Network = network;

      Chain = new Headerchain(
        genesisBlock.Header, 
        checkpoints, 
        network);

      HeaderchainDataPipe = new BatchDataPipe(Chain);

      UTXO = new UTXOTable(this);

      ArchiveLoader = new ArchiveBlockLoader(this);

      NetworkGateway = new GatewayBlockchainNetwork(
        this, 
        network, 
        Chain);
    }


    
    public async Task Start()
    {      
      await HeaderchainDataPipe.Start();

      Console.WriteLine("Chain loaded to hight {0}", 
        Chain.GetHeight());

      await NetworkGateway.SyncHeaderchain();

      Console.WriteLine("Chain synced to hight {0}",
        Chain.GetHeight());

      //await UTXODataPipe.Start();
      //await SyncUTXO();
    }



    IEnumerable<byte[]> GetLocatorHashes()
    {
      lock (LOCK_Chain)
      {
        return Chain.Locator.GetHeaderHashes();
      }
    }

    async Task SyncUTXO()
    {
      // channel verwaltung für utxosync
    }

  }
}