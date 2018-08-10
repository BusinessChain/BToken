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
    public Network NetworkAdapter;
    
    Blockchain Blockchain;
    readonly ChainBlock GenesisBlock = new BitcoinGenesisChainBlock();
    readonly UInt256 CheckpointHash = new UInt256("000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39");

    UnspentTXOutputs UTXO;

    public Bitcoin()
    {
      NetworkAdapter = new Network(/* Bitcoin configuration */);
      Blockchain = new Blockchain(GenesisBlock, CheckpointHash, NetworkAdapter);
      UTXO = new UnspentTXOutputs(Blockchain, NetworkAdapter);
    }

    public async Task startAsync()
    {
      await NetworkAdapter.startAsync(Blockchain.getHeight());
      await Blockchain.startAsync();
      //await UTXO.startAsync();
      Console.Write("helle");
    }
  }
}
