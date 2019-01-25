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
        var tXInputsUnfunded = new Dictionary<UInt256, List<TXInput>>();
        Blockchain.BlockStream blockStream = Blockchain.GetBlockStream();
        NetworkBlock block = await blockStream.ReadBlockAsync();
        
        while (block != null)
        {
          Console.WriteLine("Building UTXO block: '{0}', height: '{1}', size: '{2}'",
            blockStream.Location.Hash.ToString(),
            blockStream.Location.Height,
            block.Payload.Length);
          
          ValidatePayload(block, out List<TX> tXs);

          var uTXOTransaction = new UTXOTransaction(this, tXs, blockStream.Location.Hash);
          await uTXOTransaction.BuildAsync(tXInputsUnfunded);

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

    void ValidatePayload(NetworkBlock block, out List<TX> tXs)
    {
      tXs = PayloadParser.Parse(block.Payload);
      UInt256 merkleRootHashComputed = PayloadParser.ComputeMerkleRootHash(tXs);
      if (!merkleRootHashComputed.Equals(block.Header.MerkleRoot))
      {
        throw new UTXOException("Payload corrupted.");
      }
    }
    
    async Task Update(NetworkBlock block, UInt256 hash)
    {
      ValidatePayload(block, out List<TX> tXs);

      var uTXOTransaction = new UTXOTransaction(this, tXs, hash);
      await uTXOTransaction.InsertAsync();
    }
        
    async Task<TX> ReadTXAsync(UInt256 tXHash, byte[] headerIndex)
    {
      List<NetworkBlock> blocks = await Blockchain.ReadBlocksAsync(headerIndex);

      foreach (NetworkBlock block in blocks)
      {
        ValidatePayload(block, out List<TX> tXs);
        
        foreach (TX tX in tXs)
        {
          if(tX.GetTXHash().Equals(tXHash))
          {
            return tX;
          }
        }
      }

      return null;
    }
  }
}
