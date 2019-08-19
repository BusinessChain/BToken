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
    NetworkBlockLoader NetworkLoader;
    BitcoinGenesisBlock GenesisBlock;
    
    const int HASH_BYTE_SIZE = 32;
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
      NetworkLoader = new NetworkBlockLoader(this);
    }

    public async Task StartAsync()
    {
      await Chain.StartAsync();
      
      UTXO.StartAsync();

      await ArchiveLoader.RunAsync();

      NetworkLoader.Start();
    }
  }
}