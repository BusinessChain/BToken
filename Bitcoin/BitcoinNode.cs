using System.Diagnostics;

using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken.Bitcoin
{
  public class BitcoinNode
  {
    public Network Network { get; private set; }
    public Blockchain Blockchain { get; private set; }
    UnspentTXOutputs UTXO;

    List<BlockLocation> Checkpoints = new List<BlockLocation>()
      {
        new BlockLocation(height : 11111, hash : new UInt256("0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d")),
        new BlockLocation(height : 250000, hash : new UInt256("000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214")),
        new BlockLocation(height : 535419, hash : new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39"))
      };

    public BitcoinNode()
    {
      Network = new Network();

      Blockchain = new Blockchain(new BitcoinGenesisBlock(), Network, new BitcoinBlockPayloadParser(), Checkpoints);

      UTXO = new UnspentTXOutputs(Blockchain, Network);
    }

    public async Task StartAsync()
    {
      await Blockchain.StartAsync().ConfigureAwait(false);
      //await UTXO.startAsync();
    }
  }
}
