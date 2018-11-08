using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public class UnspentTXOutputs
  {
    INetwork Network;
    Blockchain Blockchain;


    // API
    public UnspentTXOutputs(Blockchain blockchain, INetwork network)
    {
      Network = network;
      Blockchain = blockchain;
    }


    public async Task startAsync()
    {
      // Build table
    }


    //public async Task<UTXOMessage> readMessageAsync()
    //{
    //  while (true)
    //  {
    //    BlockchainMessage messageFromBlockchain = await Blockchain.readMessageAsync();

    //    switch (messageFromBlockchain)
    //    {
    //      case GetUTXOMessage getUTXOMessage:
    //        // Message handlen
    //        break;
    //      case BlockPayloadMessage blockPayloadMessage:
    //        //neue Transaktionen verarbeiten.
    //        //Danach UTXOMessage returnen mit neuen Unspent und verbrauchten Unspent ausgegeben. Info für Wallet.
    //        return new UTXOMutations : UTXOMessage
    //                default:
    //        throw new NotSupportedException("UTXO received unknown BlockchainMessage from Blockchain.");
    //    }

    //    return new UTXOMessage(messageFromBlockchain);
    //  }
    //}


  }
}
