using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net;
using System.Net.Sockets;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  partial class Bitcoin
  {
    public Network Network;
    
    Blockchain Blockchain;

    readonly UInt256 CheckpointHash = new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39");
    readonly Blockchain.ChainBlock GenesisBlock = new BitcoinGenesisChainBlock();

    UnspentTXOutputs UTXO;

    public Bitcoin()
    {
      Network = new Network(/* Bitcoin configuration */);

      Blockchain = new Blockchain(GenesisBlock, CheckpointHash, Network);

      UTXO = new UnspentTXOutputs(Blockchain, Network);
    }

    public async Task startAsync()
    {
      await Blockchain.startAsync();
      //await UTXO.startAsync();
    }
  }
}
