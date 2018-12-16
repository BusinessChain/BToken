using System.Diagnostics;

using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Accounting.Bitcoin;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  public class BitcoinNode
  {
    public Network Network { get; private set; }
    public Blockchain Blockchain { get; private set; }
    UTXO UTXO;

    BitcoinPayloadParser PayloadParser = new BitcoinPayloadParser();
    BitcoinGenesisBlock GenesisBlock = new BitcoinGenesisBlock();
    List<ChainLocation> Checkpoints = new List<ChainLocation>()
      {
        new ChainLocation(height : 11111, hash : new UInt256("0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d")),
        new ChainLocation(height : 250000, hash : new UInt256("000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214")),
        new ChainLocation(height : 535419, hash : new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39"))
      };

    public BitcoinNode()
    {
      Network = new Network();
      Blockchain = new Blockchain(GenesisBlock, Network, Checkpoints, PayloadParser);
      UTXO = new UTXO(Blockchain, Network, PayloadParser);
    }

    public async Task StartAsync()
    {
      Network.Start();
      await Blockchain.StartAsync();
      await UTXO.StartAsync();
    }
  }
}
