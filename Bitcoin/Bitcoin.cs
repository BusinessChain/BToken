using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  partial class Bitcoin
  {
    public Network Network { get; private set; }
    public Blockchain Blockchain { get; private set; }
    UnspentTXOutputs UTXO;

    Blockchain.ChainBlock GenesisBlock = new BitcoinGenesisBlock();
    List<Blockchain.BlockLocation> Checkpoints = new List<Blockchain.BlockLocation>()
      {
        new Blockchain.BlockLocation(height : 11111, hash : new UInt256("0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d")),
        new Blockchain.BlockLocation(height : 250000, hash : new UInt256("000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214")),
        new Blockchain.BlockLocation(height : 535419, hash : new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39"))
      }; // ascending sort in height mandatory

    public Bitcoin()
    {
      Network = new Network();

      Blockchain = new Blockchain(Network, GenesisBlock, Checkpoints);

      UTXO = new UnspentTXOutputs(Blockchain, Network);
    }

    public async Task startAsync()
    {
      await Blockchain.startAsync();
      //await UTXO.startAsync();
    }
  }
}
