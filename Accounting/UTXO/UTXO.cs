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
    UTXOParser Parser;
    UTXOArchiver Archiver;
    Network Network;

    Dictionary<byte[], byte[]> UTXOTable;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      Parser = new UTXOParser();
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
        var inputsUnfunded = new Dictionary<byte[], List<TXInput>>(new EqualityComparerByteArray());

        var headerStreamer = new Headerchain.HeaderStream(Headerchain);
        headerStreamer.ReadHeader(out UInt256 hash, out uint height);

        while (hash != null)
        {
          Block block = await GetBlockAsync(hash);

          Console.WriteLine("Building UTXO block: '{0}', height: '{1}'",
            hash.ToString(),
            height);
          
          var uTXOTransaction = new UTXOTransaction(this, block);
          await uTXOTransaction.BuildAsync(inputsUnfunded);
          await Archiver.ArchiveBlockAsync(block);

          headerStreamer.ReadHeader(out hash, out height);
        }

        Console.WriteLine("UTXO build complete");
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }
    async Task<Block> GetBlockAsync(UInt256 hash)
    {
      try
      {
        NetworkBlock block = await Archiver.ReadBlockAsync(hash);
        ValidateHeaderHash(block.Header, hash);
               
        List<TX> tXs = Parser.Parse(block.Payload);
        ValidateMerkleRoot(block.Header.MerkleRoot, tXs, out List<byte[]> tXHashes);
        return new Block(block.Header, hash, tXs, tXHashes);
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
    async Task<Block> DownloadBlockAsync(UInt256 hash)
    {
      var sessionBlockDownload = new SessionBlockDownload(hash);
      await Network.ExecuteSessionAsync(sessionBlockDownload);
      NetworkBlock block = sessionBlockDownload.Block;

      ValidateHeaderHash(block.Header, hash);
      List<TX> tXs = Parser.Parse(block.Payload);
      ValidateMerkleRoot(block.Header.MerkleRoot, tXs, out List<byte[]> tXHashes);
      return new Block(block.Header, hash, tXs, tXHashes);
    }

    public async Task NotifyBlockHeadersAsync(List<UInt256> hashes, INetworkChannel channel)
    {
      foreach(UInt256 hash in hashes)
      {
        Block block = await GetBlockAsync(hash);

        var uTXOTransaction = new UTXOTransaction(this, block);
        await uTXOTransaction.InsertAsync();
      }
    }

    async Task<TX> ReadTXAsync(byte[] tXHash, byte[] headerIndex)
    {
      List<Headerchain.ChainHeader> headers = Headerchain.ReadHeaders(headerIndex);

      foreach (var header in headers)
      {
        if (!Headerchain.TryGetHeaderHash(header, out UInt256 hash))
        {
          hash = header.NetworkHeader.ComputeHash();
        }

        Block block = await GetBlockAsync(hash);

        for(int t = 0; t < block.TXs.Count; t++)
        {
          if (new UInt256(block.TXHashes[t]).Equals(new UInt256(tXHash)))
          {
            return block.TXs[t];
          }
        }
      }

      return null;
    }

    void ValidateHeaderHash(NetworkHeader header, UInt256 hash)
    {
      if (!hash.Equals(header.ComputeHash()))
      {
        throw new UTXOException("Unexpected header hash.");
      }
    }
    void ValidateMerkleRoot(UInt256 merkleRoot, List<TX> tXs, out List<byte[]> tXHashes)
    {
      if (!merkleRoot.Equals(Parser.ComputeMerkleRootHash(tXs, out tXHashes)))
      {
        throw new UTXOException("Payload corrupted.");
      }
    }
  }
}
