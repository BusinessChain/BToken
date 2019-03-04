using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    UTXOParser Parser;
    Network Network;
    
    static int CountIndexKeyBytesMin = 4;
    static int CountHeaderIndexBytes = 4;

    Dictionary<byte[], byte[]> UTXOs;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      Parser = new UTXOParser();
      Network = network;

      UTXOs = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
    }
    
    public async Task StartAsync()
    {
      var uTXOBuilder = new UTXOBuilder(
        this,
        new Headerchain.HeaderStream(Headerchain));
      
      await uTXOBuilder.BuildAsync();
    }    

    async Task<Block> GetBlockAsync(UInt256 hash)
    {
      try
      {
        NetworkBlock block = await BlockArchiver.ReadBlockAsync(hash);

        ValidateHeaderHash(block.Header, hash);
               
        List<TX> tXs = Parser.Parse(block.Payload);
        ValidateMerkleRoot(block.Header.MerkleRoot, tXs, out List<byte[]> tXHashes);
        return new Block(block.Header, hash, tXs, tXHashes);
      }
      catch (UTXOException)
      {
        BlockArchiver.DeleteBlock(hash);

        return await DownloadBlockAsync(hash);
      }
      catch (ArgumentException)
      {
        BlockArchiver.DeleteBlock(hash);

        return await DownloadBlockAsync(hash);
      }
      catch (IOException)
      {
        return await DownloadBlockAsync(hash);
      }
    }
    async Task<Block> DownloadBlockAsync(UInt256 hash)
    {
      Console.WriteLine("Download block '{0}'", hash);
      var sessionBlockDownload = new SessionBlockDownload(hash);
      await Network.ExecuteSessionAsync(sessionBlockDownload);
      NetworkBlock networkBlock = sessionBlockDownload.Block;
      
      ValidateHeaderHash(networkBlock.Header, hash);
      List<TX> tXs = Parser.Parse(networkBlock.Payload);
      ValidateMerkleRoot(networkBlock.Header.MerkleRoot, tXs, out List<byte[]> tXHashes);

      Block archiverBlock = new Block(networkBlock.Header, hash, tXs, tXHashes);
      await BlockArchiver.ArchiveBlockAsync(archiverBlock);
      return archiverBlock;
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

    public async Task NotifyBlockHeadersAsync(List<UInt256> hashes, INetworkChannel channel)
    {
      foreach(UInt256 hash in hashes)
      {
        Block block = await GetBlockAsync(hash);

        var uTXOTransaction = new UTXOTransaction(this, block);
        await uTXOTransaction.InsertAsync(UTXOs);
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

    static void SpendOutputsBits(byte[] uTXO, List<TXInput> inputs)
    {
      for (int i = 0; i < inputs.Count; i++)
      {
        int byteIndex = inputs[i].IndexOutput / 8 + CountHeaderIndexBytes;
        int bitIndex = inputs[i].IndexOutput % 8;

        var bitMask = (byte)(0x01 << bitIndex);
        if ((uTXO[byteIndex] & bitMask) != 0x00)
        {
          throw new UTXOException(string.Format("Output '{0}'-'{1}' already spent.",
          Bytes2HexStringReversed(inputs[i].TXIDOutput),
          inputs[i].IndexOutput));
        }
        uTXO[byteIndex] |= bitMask;
      }
    }
    static bool AreAllOutputBitsSpent(byte[] uTXO)
    {
      for (int i = CountHeaderIndexBytes; i < uTXO.Length; i++)
      {
        if (uTXO[i] != 0xFF) { return false; }
      }

      return true;
    }

    static string Bytes2HexStringReversed(byte[] bytes)
    {
      var bytesReversed = new byte[bytes.Length];
      bytes.CopyTo(bytesReversed, 0);
      Array.Reverse(bytesReversed);
      return new SoapHexBinary(bytesReversed).ToString();
    }
  }
}