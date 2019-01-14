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
        Blockchain.BlockReader blockStream = Blockchain.GetBlockReader();
        NetworkBlock block = await blockStream.ReadBlockNextInChainAsync();

        while (block != null)
        {
          Console.WriteLine("Building UTXO block: '{0}', height: '{1}', size: '{2}'",
            blockStream.Location.Hash.ToString(),
            blockStream.Location.Height,
            block.Payload.Length);

          List<TX> tXs = PayloadParser.Parse(block.Payload);
          ValidatePayload(block.Header.MerkleRoot, tXs);
          Build(tXs, blockStream.Location.Hash);

          block = await blockStream.ReadBlockNextInChainAsync();
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.WriteLine("UTXO syncing completed");


      // Listen to new blocks.
    }
    void ValidatePayload(UInt256 merkleRootHash, List<TX> bitcoinTXs)
    {
      UInt256 merkleRootHashComputed = PayloadParser.ComputeMerkleRootHash(bitcoinTXs);
      if (!merkleRootHashComputed.IsEqual(merkleRootHash))
      {
        throw new UTXOException("Corrupted payload.");
      }
    }

    void Build(List<TX> bitcoinTXs, UInt256 blockHeaderHash)
    {
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

    async Task Update(NetworkBlock block, UInt256 hash)
    {
      List<TX> tXs = PayloadParser.Parse(block.Payload);
      ValidatePayload(block.Header.MerkleRoot, tXs);

      var uTXOBlockTransaction = new UTXOTransaction(tXs, hash);
      await uTXOBlockTransaction.ProcessAsync();
    }
    
    static bool IsOutputSpent(byte[] tXOutputIndex, int index)
    {
      int byteIndex = index / 8;
      int bitIndex = index % 8;
      byte maskFlag = (byte)(0x01 << bitIndex);
      return (maskFlag & tXOutputIndex[byteIndex]) != 0x00;
    }

    async Task<TX> ReadTXAsync(UInt256 tXHash, byte[] headerIndex)
    {
      List<NetworkBlock> blocks = await Blockchain.ReadBlocksAsync(headerIndex);

      foreach (NetworkBlock block in blocks)
      {
        List<TX> tXs = PayloadParser.Parse(block.Payload);
        ValidatePayload(block.Header.MerkleRoot, tXs);

        foreach (TX tX in tXs)
        {
          if(tX.GetTXHash().IsEqual(tXHash))
          {
            return tX;
          }
        }
      }

      return null;
    }
  }
}
