using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting.UTXO
{
  partial class UTXO
  {
    INetwork Network;
    Blockchain Blockchain;
    PayloadParser PayloadParser;

    Dictionary<byte[], byte[]> UTXOTable;


    public UTXO(Blockchain blockchain, INetwork network, PayloadParser payloadParser)
    {
      Network = network;
      Blockchain = blockchain;
      PayloadParser = payloadParser;

      UTXOTable = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
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
      List<TX> bitcoinTXs = PayloadParser.Parse(block.Payload);
      var uTXOBlockTransaction = new UTXOTransaction(bitcoinTXs, blockHeaderHash);

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
      List<TX> bitcoinTXs = PayloadParser.Parse(block.Payload);
      var uTXOBlockTransaction = new UTXOTransaction(bitcoinTXs, headerHash);
      uTXOBlockTransaction.ProcessAsync();
    }
    
    static bool IsOutputSpent(byte[] tXOutputIndex, int index)
    {
      int byteIndex = index / 8;
      int bitIndex = index % 8;
      byte maskFlag = (byte)(0x01 << bitIndex);
      return (maskFlag & tXOutputIndex[byteIndex]) != 0x00;
    }

  }
}
