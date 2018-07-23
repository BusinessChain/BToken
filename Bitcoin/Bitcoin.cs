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
  public class Bitcoin
  {
    NetworkAdapter NetworkAdapter;
    
    Blockchain Blockchain;
    #region GenesisBlock
    static readonly ChainBlock GenesisBlock = new ChainBlock(
      new ChainHeader(
        new UInt256("000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f"),
        null,
        0,
        new UInt256("00000000FFFF0000000000000000000000000000000000000000000000000000"),
        1,
        1,
        1231006505),
      new List<TX>());
    #endregion

    UnspentTXOutputs UTXO;

    public Bitcoin()
    {
      NetworkAdapter = new NetworkAdapter(/* Bitcoin configuration */);
      Blockchain = new Blockchain(GenesisBlock, NetworkAdapter);
      UTXO = new UnspentTXOutputs(Blockchain, NetworkAdapter);
    }

    public async void startAsync()
    {
      await NetworkAdapter.startAsync(Blockchain.getBestHeight());
      await Blockchain.startAsync();
      await UTXO.startAsync();
    }
  }
}
