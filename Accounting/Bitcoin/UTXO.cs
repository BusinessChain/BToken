using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting.Bitcoin
{
  partial class UTXO
  {
    INetwork Network;
    Blockchain Blockchain;
    BitcoinPayloadParser PayloadParser;

    Dictionary<string, string> UnspentOutputs = new Dictionary<string, string>();
    Dictionary<string, string> SpentOutputs = new Dictionary<string, string>();


    // API
    public UTXO(Blockchain blockchain, INetwork network, BitcoinPayloadParser payloadParser)
    {
      Network = network;
      Blockchain = blockchain;
      PayloadParser = payloadParser;
    }


    public async Task StartAsync()
    {
      // Load from UTXO archive

      try
      {
        Blockchain.BlockStream blockStream = Blockchain.GetBlockStream();
        NetworkBlock block = await blockStream.ReadBlockAsync().ConfigureAwait(false);

        while (block != null)
        {
          BuildBackwards(block, blockStream.Location.Hash);
          Console.WriteLine("Built UTXO block: '{0}', height: '{1}'", blockStream.Location.Hash.ToString(), blockStream.Location.Height);

          block = await blockStream.ReadBlockAsync().ConfigureAwait(false);
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.WriteLine("UTXO syncing completed");

      // Listener to new blocks.
    }

    void BuildBackwards(NetworkBlock block, UInt256 headerHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);

      var utxoTransaction = new UTXOTransaction(bitcoinTXs, headerHash);
      
      foreach (KeyValuePair<string, string> outpoint in utxoTransaction.UnspentOutputs)
      {
        if (UnspentOutputs.ContainsKey(outpoint.Key))
        {
          throw new UTXOException(string.Format("Encountered ambiguous output '{0}' in block '{1}' while building UTXO", outpoint.Key, headerHash));
        }
        else
        {
          if(SpentOutputs.ContainsKey(outpoint.Key))
          {
            SpentOutputs.Remove(outpoint.Key);
          }
          else
          {
            UnspentOutputs.Add(outpoint.Key, outpoint.Value);
          }
        }
      }

      foreach (KeyValuePair<string, string> outpoint in utxoTransaction.SpentOutputs)
      {
        if (UnspentOutputs.ContainsKey(outpoint.Key))
        {
          throw new UTXOException(string.Format("Encountered ambiguous output '{0}' in block '{1}' while building UTXO", outpoint.Key, headerHash));
        }
        else if (!IsOutputCoinbase(outpoint))
        {
          if (SpentOutputs.ContainsKey(outpoint.Key))
          {
            throw new UTXOException(string.Format("Double spend detected at output '{0}' in block '{1}' while building UTXO", outpoint.Key, headerHash));
          }
          else
          {
            SpentOutputs.Add(outpoint.Key, outpoint.Value);
          }
        }
      }
    }

    void InsertBlock(NetworkBlock block, UInt256 headerHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);

      var utxoTransaction = new UTXOTransaction(bitcoinTXs, headerHash);

      foreach (KeyValuePair<string, string> outpoint in utxoTransaction.UnspentOutputs)
      {
        if (UnspentOutputs.ContainsKey(outpoint.Key))
        {
          throw new UTXOException(string.Format("ambiguous output '{0}' in block '{1}'", outpoint.Key, headerHash));
        }
        else
        {
          UnspentOutputs.Add(outpoint.Key, outpoint.Value);
        }
      }

      foreach (KeyValuePair<string, string> outpoint in utxoTransaction.SpentOutputs)
      {
        if (UnspentOutputs.ContainsKey(outpoint.Key))
        {
          UnspentOutputs.Remove(outpoint.Key);
        }
        else if (!IsOutputCoinbase(outpoint))
        {
          throw new UTXOException(string.Format("Attempts to spend nonexistant output '{0}' in block '{1}'", outpoint.Key, headerHash));
        }
      }
    }

    bool IsOutputCoinbase(KeyValuePair<string, string> outpoint)
    {
      return outpoint.Key == "0000000000000000000000000000000000000000000000000000000000000000.4294967295";
    }

  }
}
