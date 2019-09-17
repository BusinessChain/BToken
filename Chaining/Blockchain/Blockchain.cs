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
    BatchDataPipe UTXODataPipe;
    UTXOTable UTXO;
    readonly object LOCK_Chain = new object();

    BatchDataPipe HeaderchainDataPipe;
    Headerchain Chain;
        
    const int HASH_BYTE_SIZE = 32;
    const int COUNT_TXS_IN_BATCH_FILE = 50000;
        

    public Blockchain(
      BitcoinGenesisBlock genesisBlock,
      List<HeaderLocation> checkpoints, 
      Network network)
    {
      Chain = new Headerchain(
        genesisBlock.Header,
        checkpoints);
      
      HeaderchainDataPipe = new BatchDataPipe(
        Chain,
        new GatewayHeaderchain(this, network, Chain));


      UTXO = new UTXOTable(this);

      UTXODataPipe = new BatchDataPipe(
        UTXO,
        new GatewayUTXO(this, network, UTXO));
    }


    
    public async Task Start()
    {      
      await HeaderchainDataPipe.Start();
      await UTXODataPipe.Start();
    }



    IEnumerable<byte[]> GetLocatorHashes()
    {
      lock (LOCK_Chain)
      {
        return Chain.Locator.GetHeaderHashes();
      }
    }

  }
}