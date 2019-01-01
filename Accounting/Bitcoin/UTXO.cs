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

    Dictionary<string, UInt256> TXOutputs = new Dictionary<string, UInt256>();
    public Dictionary<string, TXInput> TXInputs = new Dictionary<string, TXInput>();
    Dictionary<UInt256, byte[]> UnspentTXOutputs = new Dictionary<UInt256, byte[]>();


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
        NetworkBlock block = await blockStream.ReadBlockAsync();

        while (block != null)
        {
          Console.WriteLine("Building UTXO block: '{0}', height: '{1}', size: '{2}'",
            blockStream.Location.Hash.ToString(),
            blockStream.Location.Height,
            block.Payload.Length);

          Build(block, blockStream.Location.Hash);

          block = await blockStream.ReadBlockAsync();
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.WriteLine("UTXO syncing completed");


      // Listen to new blocks.
    }

    void Build(NetworkBlock block, UInt256 blockHeaderHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);
      var uTXOBlockTransaction = new UTXOTransaction(this, bitcoinTXs, blockHeaderHash);

      //foreach (KeyValuePair<string, TXOutput> tXOutput in uTXOBlockTransaction.TXOutputs)
      //{
      //  if (!TXInputs.Remove(tXOutput.Key))
      //  {
      //    TXOutputs.Add(tXOutput.Key, blockHeaderHash);
      //  }
      //}

      //foreach (KeyValuePair<string, TXInput> tXInputBlockTransaction in uTXOBlockTransaction.TXInputs)
      //{
      //  TXInputs.Add(tXInputBlockTransaction.Key, tXInputBlockTransaction.Value);
      //}
    }

    void Update(NetworkBlock block, UInt256 headerHash)
    {
      List<BitcoinTX> bitcoinTXs = PayloadParser.Parse(block.Payload);
      var uTXOBlockTransaction = new UTXOTransaction(this, bitcoinTXs, headerHash);
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
      return GetOutputReference(txInput.TXIDOutput, txInput.IndexOutput);
    }
    static string GetOutputReference(UInt256 txid, int index)
    {
      return txid + "." + index;
    }
  }
}
