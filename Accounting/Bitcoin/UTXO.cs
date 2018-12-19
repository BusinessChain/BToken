using System.Diagnostics;

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

    Dictionary<string, UInt256> UnspentOutputs = new Dictionary<string, UInt256>();
    public Dictionary<string, TXInput> TXInputs = new Dictionary<string, TXInput>();


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
          Console.WriteLine("Number of UTXOs : '{0}', Number of inputs: '{1}'", UnspentOutputs.Count, TXInputs.Count);
          Console.WriteLine("Start building UTXO block: '{0}', height: '{1}', size: '{2}'",
            blockStream.Location.Hash.ToString(),
            blockStream.Location.Height,
            block.Payload.Length);
          Stopwatch stopWatch = Stopwatch.StartNew();

          BuildUTXO(block, blockStream.Location.Hash);

          stopWatch.Stop();
          Console.WriteLine("Built UTXO block: '{0}', height: '{1}', size: '{2}' elapsed time '{3}'", 
            blockStream.Location.Hash.ToString(), 
            blockStream.Location.Height, 
            block.Payload.Length,
            stopWatch.Elapsed.ToString());

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

    void BuildUTXO(NetworkBlock block, UInt256 headerHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);

      var uTXOBlockTransaction = new UTXOTransaction(bitcoinTXs, headerHash);

      foreach (KeyValuePair<string, TXOutput> tXOutput in uTXOBlockTransaction.TXOutputs)
      {
        if (!TXInputs.Remove(tXOutput.Key))
        {
          UnspentOutputs.Add(tXOutput.Key, headerHash);
        }
      }

      foreach (KeyValuePair<string, TXInput> tXInputBlockTransaction in uTXOBlockTransaction.TXInputs)
      {
        try
        {
          TXInputs.Add(tXInputBlockTransaction.Key, tXInputBlockTransaction.Value);
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
    }

    async Task UpdateUTXO(NetworkBlock block, UInt256 headerHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);
      var uTXOBlockTransaction = new UTXOTransaction(bitcoinTXs, headerHash);

      foreach (KeyValuePair<string, TXOutput> outpoint in uTXOBlockTransaction.TXOutputs)
      {
        if (UnspentOutputs.ContainsKey(outpoint.Key))
        {
          throw new UTXOException(string.Format("ambiguous output '{0}' in block '{1}'", outpoint.Key, headerHash));
        }
        else
        {
          UnspentOutputs.Add(outpoint.Key, headerHash);
        }
      }

      foreach (KeyValuePair<string, TXInput> tXInput in uTXOBlockTransaction.TXInputs)
      {
        if (UnspentOutputs.TryGetValue(tXInput.Key, out UInt256 headerHashReferenced))
        {
          NetworkBlock blockReferenced = await Blockchain.GetBlockAsync(headerHashReferenced);
          TXOutput txOutput = GetTXOutput(blockReferenced, tXInput.Value);

          if (txOutput.TryUnlockScript(tXInput.Value.UnlockingScript))
          {
            UnspentOutputs.Remove(tXInput.Key);
          }
        }
        else
        {
          throw new UTXOException(string.Format("Attempt to consume spent or nonexistant output '{0}' in block '{1}'",
            tXInput.Key, headerHash));
        }
      }
    }
    TXOutput GetTXOutput(NetworkBlock blockReferenced, TXInput txInput)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(blockReferenced.Payload);

      BitcoinTX bitcoinTX = bitcoinTXs.Find(b => b.GetTXHash().IsEqual(txInput.TXID));
      return bitcoinTX.TXOutputs[(int)txInput.IndexOutput];
    }


    static UInt256 GetHashFromOutputReference(string outputReference)
    {
      string txHashString = outputReference.Split('.')[0];
      return new UInt256(txHashString);
    }
    static int GetIndexFromOutputReference(string outputReference)
    {
      string indexString = outputReference.Split('.')[1];
      return int.Parse(indexString);
    }
    static string GetOutputReference(TXInput txInput)
    {
      return GetOutputReference(txInput.TXID, txInput.IndexOutput);
    }
    static string GetOutputReference(UInt256 txid, uint index)
    {
      return txid + "." + index;
    }
  }
}
