using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
    Network Network;

    Dictionary<byte[], byte[]> UTXOTable;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      PayloadParser = new PayloadParser();
      Archiver = new UTXOArchiver(this);
      Network = network;

      UTXOTable = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
    }
    
    public async Task StartAsync()
    {
      await BuildAsync();
    }

    async Task BuildAsync()
    {
      try
      {
        var tXInputsUnfunded = new Dictionary<UInt256, List<TXInput>>();

        var headerStreamer = new Headerchain.HeaderStream(Headerchain);
        headerStreamer.ReadHeader(out UInt256 hash, out uint height);

        while (hash != null)
        {
          List<TX> tXs = await GetBlockTXsAsync(hash);

          Console.WriteLine("Building UTXO block: '{0}', height: '{1}'",
            hash.ToString(),
            height);
          
          var uTXOTransaction = new UTXOTransaction(this, tXs, hash);
          await uTXOTransaction.BuildAsync(tXInputsUnfunded);

          headerStreamer.ReadHeader(out hash, out height);
        }

        Console.WriteLine("UTXO build complete");
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }
    async Task<List<TX>> GetBlockTXsAsync(UInt256 hash)
    {
      try
      {
        NetworkBlock block = await Archiver.ReadBlockAsync(hash);
        ValidateBlock(block, hash, out List<TX> tXs);
        return tXs;
      }
      catch (UTXOException)
      {
        Archiver.DeleteBlock(hash);
        return await DownloadBlockAsync(hash);
      }
      catch (IOException)
      {
        return await DownloadBlockAsync(hash);
      }
    }
    async Task<List<TX>> DownloadBlockAsync(UInt256 hash)
    {
      var sessionBlockDownload = new SessionBlockDownload(hash);
      await Network.ExecuteSessionAsync(sessionBlockDownload);
      ValidateBlock(sessionBlockDownload.Block, hash, out List<TX> tXs);
      return tXs;
    }

    public async Task NotifyBlockHeadersAsync(List<UInt256> hashes, INetworkChannel channel)
    {
      foreach(UInt256 hash in hashes)
      {
        var sessionBlockDownload = new SessionBlockDownload(hash);

        if (!await channel.TryExecuteSessionAsync(sessionBlockDownload, default(CancellationToken)))
        {
          await Network.ExecuteSessionAsync(sessionBlockDownload);
        }

        ValidatePayloadHash(sessionBlockDownload.Block, out List<TX> tXs);
        var uTXOTransaction = new UTXOTransaction(this, tXs, hash);
        await uTXOTransaction.InsertAsync();
      }

    }

    async Task<TX> ReadTXAsync(UInt256 tXHash, byte[] headerIndex)
    {
      List<Headerchain.ChainHeader> headers = Headerchain.ReadHeaders(headerIndex);

      foreach (var header in headers)
      {
        if (!Headerchain.TryGetHeaderHash(header, out UInt256 hash))
        {
          hash = header.NetworkHeader.ComputeHeaderHash();
        }

        List<TX> tXs = await GetBlockTXsAsync(hash);
        
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

    void ValidateBlock(NetworkBlock block, UInt256 hash, out List<TX> tXs)
    {
      ValidateHeaderHash(block.Header, hash);
      ValidatePayloadHash(block, out tXs);
    }
    void ValidateHeaderHash(NetworkHeader header, UInt256 hash)
    {
      UInt256 hashComputed = header.ComputeHeaderHash();
      if (!hash.Equals(hashComputed))
      {
        throw new UTXOException("Unexpected header hash.");
      }
    }
    void ValidatePayloadHash(NetworkBlock block, out List<TX> tXs)
    {
      tXs = PayloadParser.Parse(block.Payload, out UInt256 merkleRootHash);
      if (!merkleRootHash.Equals(block.Header.MerkleRoot))
      {
        throw new UTXOException("Payload corrupted.");
      }
    }
  }
}
