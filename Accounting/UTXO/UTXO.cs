using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    PayloadParser PayloadParser;
    UTXOArchiver Archiver;

    Dictionary<byte[], byte[]> UTXOTable;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      PayloadParser = new PayloadParser();
      Archiver = new UTXOArchiver(this, network);

      UTXOTable = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
    }
    
    public async Task StartAsync()
    {
      // Load from UTXO archive

      try
      {
        var tXInputsUnfunded = new Dictionary<UInt256, List<TXInput>>();

        var headerStreamer = new Headerchain.HeaderStream(Headerchain);
        headerStreamer.ReadHeader(out ChainLocation location);

        while (location != null)
        {
          NetworkBlock block = await Archiver.ReadBlockAsync(location.Hash);

          Console.WriteLine("Building UTXO block: '{0}', height: '{1}', size: '{2}'",
            location.Hash.ToString(),
            location.Height,
            block.Payload.Length);
          
          ValidatePayload(block, out List<TX> tXs);

          var uTXOTransaction = new UTXOTransaction(this, tXs, location.Hash);
          await uTXOTransaction.BuildAsync(tXInputsUnfunded);

          headerStreamer.ReadHeader(out location);
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.WriteLine("UTXO syncing completed");
    }

    public async Task NotifyBlockHeadersAsync(List<UInt256> hashes, INetworkChannel channel)
    {
      foreach(UInt256 hash in hashes)
      {
        NetworkBlock block = await Archiver.ReadBlockAsync(hash, channel);
        ValidatePayload(block, out List<TX> tXs);
        var uTXOTransaction = new UTXOTransaction(this, tXs, hash);
        await uTXOTransaction.InsertAsync();
      }

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

    async Task<TX> ReadTXAsync(UInt256 tXHash, byte[] headerIndex)
    {
      List<Headerchain.ChainHeader> headers = Headerchain.ReadHeaders(headerIndex);
      var blocks = new List<NetworkBlock>();

      foreach (var header in headers)
      {
        if (!Headerchain.TryGetHeaderHash(header, out UInt256 hash))
        {
          hash = header.NetworkHeader.ComputeHeaderHash();
        }
        NetworkBlock block = await Archiver.ReadBlockAsync(hash);
        ValidateHeaderHash(hash, block);
        blocks.Add(block);
      }

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

    static void ValidateHeaderHash(UInt256 hash, NetworkBlock block)
    {
      UInt256 hashComputed = block.Header.ComputeHeaderHash();
      if (!hash.Equals(hashComputed))
      {
        throw new ChainException(ChainCode.INVALID);
      }
    }
  }
}
