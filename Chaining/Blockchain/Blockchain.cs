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
    readonly object LOCK_Chain = new object();
    Headerchain Chain;
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
      GenesisBlock = genesisBlock;

      Chain = new Headerchain(genesisBlock.Header, checkpoints, network);

      UTXO = new UTXOTable(this);
      ArchiveLoader = new ArchiveBlockLoader(this);
      NetworkGateway = new BlockchainNetworkGateway(this, network);
    }

    public async Task StartAsync()
    {
      Chain.Load();

      await UTXO.LoadAsync();

      NetworkGateway.Start();
    }

    List<byte[]> GetChainLocator()
    {
      lock(LOCK_Chain)
      {
        return Chain.Locator.GetHeaderHashes();
      }
    }

    void InsertHeaders(List<Header> headers)
    {
      lock (LOCK_Chain)
      {
        Chain.InsertHeaders(headers);
      }
    }
  }
}