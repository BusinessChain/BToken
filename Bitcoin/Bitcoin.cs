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

      //UInt256 hash = new UInt256("0000000000000000000c4748bf61d33dd69f7b27de2acf3b7570ad00f54b6990");
      //BufferBlock<NetworkHeader> buf = NetworkAdapter.GetHeaders(hash);

      //NetworkHeader networkHeader;
      //do
      //{
      //  networkHeader = await buf.ReceiveAsync();
      //} while (networkHeader != null);
    }
  }
}
