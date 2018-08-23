using System;
using System.Threading.Tasks;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  partial class Bitcoin
  {
    public Network Network;
    
    Blockchain Blockchain;

    readonly Blockchain.BlockLocation Checkpoint = new Blockchain.BlockLocation()
    {
      Height = 535419,
      Hash = new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39")
    };
    readonly Blockchain.ChainBlock GenesisBlock = new BitcoinGenesisChainBlock();

    UnspentTXOutputs UTXO;

    public Bitcoin()
    {
      Network = new Network();

      Blockchain = new Blockchain(GenesisBlock, Checkpoint, Network);

      UTXO = new UnspentTXOutputs(Blockchain, Network);
    }

    public async Task startAsync()
    {
      await Blockchain.startAsync();
      //await UTXO.startAsync();
    }
  }
}
