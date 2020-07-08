using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using BToken.Chaining;


namespace BToken
{
  partial class Node
  {
    Blockchain Blockchain;

    Wallet Wallet;

    BitcoinGenesisBlock GenesisBlock = new BitcoinGenesisBlock();
    List<HeaderLocation> Checkpoints = new List<HeaderLocation>()
      {
        new HeaderLocation(height : 11111, hash : "0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"),
        new HeaderLocation(height : 250000, hash : "000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214"),
      };



    public Node()
    {
      Blockchain = new Blockchain(
        GenesisBlock.Header,
        GenesisBlock.BlockBytes,
        Checkpoints);

      Wallet = new Wallet();
    }

    public void Start()
    {
      Blockchain.Start();

      Wallet.GeneratePublicKey();
    }
  }
}