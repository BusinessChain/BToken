using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken
{
  public partial class BitcoinNode
  {
    Network Network;
    UTXOTable UTXO;
    Headerchain Headerchain;

    Wallet Wallet;

    BitcoinGenesisBlock GenesisBlock = new BitcoinGenesisBlock();
    List<HeaderLocation> Checkpoints = new List<HeaderLocation>()
      {
        new HeaderLocation(height : 11111, hash : "0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"),
        new HeaderLocation(height : 250000, hash : "000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214"),
        new HeaderLocation(height : 535419, hash : "000000000000000000209ecbacceb3e7b8ec520ed7f1cfafbe149dd2b9007d39")
      };

    public BitcoinNode()
    {
      Network = new Network();

      Headerchain = new Headerchain(
        GenesisBlock.Header,
        Checkpoints,
        Network);

      UTXO = new UTXOTable(
        GenesisBlock.BlockBytes,
        Headerchain,
        Network);

      Wallet = new Wallet();
    }

    public async Task StartAsync()
    {
      Network.Start();

      await Headerchain.Start();

      await UTXO.Start();

      Console.WriteLine("Blockchain sync done");

      Wallet.GeneratePublicKey();
    }
  }
}
