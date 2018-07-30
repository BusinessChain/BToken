using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

using BToken.Accounting;
using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  partial class Bitcoin
  {
    Network NetworkAdapter;
    
    Blockchain Blockchain;
    static readonly ChainBlock GenesisBlock = new BitcoinGenesisChainBlock();

    UnspentTXOutputs UTXO;

    public Bitcoin()
    {
      NetworkAdapter = new Network(/* Bitcoin configuration */);
      Blockchain = new Blockchain(GenesisBlock, NetworkAdapter);
      UTXO = new UnspentTXOutputs(Blockchain, NetworkAdapter);
    }

    public async Task startAsync()
    {
      await NetworkAdapter.startAsync(Blockchain.getHeight());
      await Blockchain.startAsync();
      await UTXO.startAsync();
    }
  }
}
