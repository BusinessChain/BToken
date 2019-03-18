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
    UTXOArchiver Archiver;

    Dictionary<int, uint> PrimaryCacheCompressed;
    Dictionary<byte[], uint> SecondaryCacheCompressed;
    Dictionary<int, byte[]> PrimaryCache;
    Dictionary<byte[], byte[]> SecondaryCache;

    const int COUNT_INTEGER_BITS = sizeof(int) * 8;
    const int COUNT_HEADERINDEX_BITS = 26;
    const int COUNT_COLLISION_BITS = 2;

    static readonly uint CountCollisionsMax = uint.MaxValue >> (COUNT_INTEGER_BITS - COUNT_COLLISION_BITS);
    static readonly int CountNonHeaderBits = COUNT_INTEGER_BITS - COUNT_HEADERINDEX_BITS;
    static readonly int CountNonCollisionBits = COUNT_INTEGER_BITS - COUNT_COLLISION_BITS;
    static readonly int CountOutputAndExcessBits = COUNT_INTEGER_BITS - COUNT_COLLISION_BITS - COUNT_HEADERINDEX_BITS;

    static readonly int CountHeaderBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;
    static readonly int CollisionBitsByteIndex = (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS + 7) / 8;
    static readonly int CountNonCollisionBitsInByte = 8 - COUNT_COLLISION_BITS;
    static readonly int CountNonHeaderBitsInByte = 8 - COUNT_HEADERINDEX_BITS % 8;
    static readonly byte MaskHeaderTailBitsInByte = (byte)(byte.MaxValue >> CountNonHeaderBitsInByte);
    static readonly int CountHeaderBitsInByte = COUNT_HEADERINDEX_BITS % 8;
    static readonly int CountTrailingBitsOfCollisionBitsInByte = CountNonHeaderBitsInByte + COUNT_COLLISION_BITS;
    static readonly int OutputBitsByteIndex = (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS) / 8;
    static readonly int CountNonOutputsBitsTail = (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS) % 8;
    static readonly byte MaskNonOutputsBitsTail = (byte)(byte.MaxValue << CountNonOutputsBitsTail);


    public UTXO(Headerchain headerchain, Network network)
    {
      if (8 < COUNT_HEADERINDEX_BITS % 8 + COUNT_COLLISION_BITS)
      {
        throw new InvalidOperationException("Collision bits should not byte overflow (including preceding tail header bits).");
      }

      Headerchain = headerchain;
      Parser = new UTXOParser();
      Network = network;

      PrimaryCacheCompressed = new Dictionary<int, uint>();
      SecondaryCacheCompressed = new Dictionary<byte[], uint>(new EqualityComparerByteArray());
      PrimaryCache = new Dictionary<int, byte[]>();
      SecondaryCache = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());

      Archiver = new UTXOArchiver();
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
    static void ValidateHeaderHash(NetworkHeader header, UInt256 hash)
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
      foreach (UInt256 hash in hashes)
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

        for (int t = 0; t < block.TXs.Count; t++)
        {
          if (new UInt256(block.TXHashes[t]).Equals(new UInt256(tXHash)))
          {
            return block.TXs[t];
          }
        }
      }

      return null;
    }

    void SpendUTXO(byte[] tXIDOutput, int outputIndex)
    {
      int primaryKey = BitConverter.ToInt32(tXIDOutput, 0);

      if (PrimaryCacheCompressed.TryGetValue(primaryKey, out uint uTXOCompressedExisting))
      {
        uint countCollisions = (uTXOCompressedExisting << CountOutputAndExcessBits) >> CountNonCollisionBits;
        if (countCollisions > 0)
        {
          if (SecondaryCacheCompressed.TryGetValue(tXIDOutput, out uint uTXOSecondaryCompressed))
          {
            SpendUTXOBit(outputIndex, ref uTXOSecondaryCompressed);

            if (AreAllOutputBitsSpent(uTXOSecondaryCompressed))
            {
              SecondaryCacheCompressed.Remove(tXIDOutput);

              if (countCollisions == CountCollisionsMax)
              {
                int countActualCollisions = 0;
                foreach(byte[] key in SecondaryCacheCompressed.Keys)
                {
                  if(BitConverter.ToInt32(key, 0) == primaryKey)
                  {
                    countActualCollisions++;
                    if(countActualCollisions == CountCollisionsMax)
                    {
                      return;
                    }
                  }
                }
              }

              SetCountCollisions(ref uTXOCompressedExisting, --countCollisions);
              PrimaryCacheCompressed[primaryKey] = uTXOCompressedExisting;
              return;
            }

            SecondaryCacheCompressed[tXIDOutput] = uTXOSecondaryCompressed;
            return;
          }

          SpendUTXOBit(outputIndex, ref uTXOCompressedExisting);

          if (AreAllOutputBitsSpent(uTXOCompressedExisting))
          {
            PrimaryCacheCompressed.Remove(primaryKey);

            foreach (KeyValuePair<byte[], uint> secondaryUTXOCompressed in SecondaryCacheCompressed)
            {
              if (primaryKey == BitConverter.ToInt32(secondaryUTXOCompressed.Key, 0))
              {
                SecondaryCacheCompressed.Remove(secondaryUTXOCompressed.Key);
                uint secondaryUTXOCompressedValue = secondaryUTXOCompressed.Value;
                SetCountCollisions(ref secondaryUTXOCompressedValue, --countCollisions);
                PrimaryCacheCompressed.Add(primaryKey, secondaryUTXOCompressedValue);
                break;
              }
            }
          }
          else
          {
            PrimaryCacheCompressed[primaryKey] = uTXOCompressedExisting;
          }
          return;
        }

        SpendUTXOBit(outputIndex, ref uTXOCompressedExisting);

        if (AreAllOutputBitsSpent(uTXOCompressedExisting))
        {
          PrimaryCacheCompressed.Remove(primaryKey);
        }
        else
        {
          PrimaryCacheCompressed[primaryKey] = uTXOCompressedExisting;
        }
      }
      else if(PrimaryCache.TryGetValue(primaryKey, out byte[] uTXOExisting))
      {
        uint countCollisions = 
          (uint)(uTXOExisting[CollisionBitsByteIndex] << CountTrailingBitsOfCollisionBitsInByte) 
          >> CountNonCollisionBitsInByte;

        if (countCollisions > 0)
        {
          if (SecondaryCache.TryGetValue(tXIDOutput, out byte[] uTXOSecondary))
          {
            SpendUTXOBit(outputIndex, uTXOSecondary);

            if (AreAllOutputBitsSpent(uTXOSecondary))
            {
              SecondaryCache.Remove(tXIDOutput);

              if (countCollisions == CountCollisionsMax)
              {
                int countActualCollisions = 0;
                foreach (byte[] key in SecondaryCacheCompressed.Keys)
                {
                  if (BitConverter.ToInt32(key, 0) == primaryKey)
                  {
                    countActualCollisions++;
                    if (countActualCollisions == CountCollisionsMax)
                    {
                      return;
                    }
                  }
                }
              }

              SetCountCollisions(ref uTXOCompressedExisting, --countCollisions);
              PrimaryCacheCompressed[primaryKey] = uTXOCompressedExisting;
              return;
            }

            SecondaryCache[tXIDOutput] = uTXOSecondary;
            return;
          }

          SpendUTXOBit(outputIndex, uTXOExisting);

          if (AreAllOutputBitsSpent(uTXOExisting))
          {
            PrimaryCache.Remove(primaryKey);

            foreach (KeyValuePair<byte[], byte[]> secondaryUTXO in SecondaryCache)
            {
              if (primaryKey == BitConverter.ToInt32(secondaryUTXO.Key, 0))
              {
                SecondaryCache.Remove(secondaryUTXO.Key);
                byte[] secondaryUTXOValue = secondaryUTXO.Value;
                SetCountCollisions(secondaryUTXOValue, --countCollisions);
                PrimaryCache.Add(primaryKey, secondaryUTXOValue);
                break;
              }
            }
          }
          else
          {
            PrimaryCache[primaryKey] = uTXOExisting;
          }

          return;
        }

        SpendUTXOBit(outputIndex, uTXOExisting);

        if (AreAllOutputBitsSpent(uTXOExisting))
        {
          PrimaryCache.Remove(primaryKey);
        }
        else
        {
          PrimaryCache[primaryKey] = uTXOExisting;
        }
      }
      else
      {
        throw new UTXOException(
          "TXInput references spent or nonexistant TX.");
      }
    }
    static void SpendUTXOBit(int outputIndex, ref uint uTXO)
    {
      uint mask = ((uint)1 << (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS + outputIndex));
      uTXO |= mask;
    }
    static void SpendUTXOBit(int outputIndex, byte[] uTXO)
    {
      int byteIndex = (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS + outputIndex) / 8;
      int bitIndex = (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS + outputIndex) % 8;

      var bitMask = (byte)(0x01 << bitIndex);
      if ((uTXO[byteIndex] & bitMask) != 0x00)
      {
        throw new UTXOException(string.Format(
          "Output index '{0}' already spent.", outputIndex));
      }

      uTXO[byteIndex] |= bitMask;
    }

    static void SetCountCollisions(byte[] uTXO, uint countCollisions)
    {
      uTXO[CollisionBitsByteIndex] &=
        (byte)((byte.MaxValue >> CountNonHeaderBitsInByte) |
        (byte.MaxValue << (CountHeaderBitsInByte + COUNT_COLLISION_BITS)));

      uTXO[CollisionBitsByteIndex] |= (byte)(countCollisions << CountHeaderBitsInByte);
    }
    static void SetCountCollisions(ref uint uTXO, uint countCollisions)
    {
      uTXO &=
        (uint.MaxValue >> CountNonHeaderBits) |
        (uint.MaxValue << (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS));

      uTXO |= countCollisions << COUNT_HEADERINDEX_BITS;
    }
    static bool AreAllOutputBitsSpent(uint uTXO)
    {
      uint mask = uint.MaxValue >> (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS);
      mask <<= (COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS);

      return (uTXO & mask) == mask;
    }
    static bool AreAllOutputBitsSpent(byte[] uTXO)
    {
      if ((uTXO[OutputBitsByteIndex] & MaskNonOutputsBitsTail) != MaskNonOutputsBitsTail)
      {
        return false;
      }

      int byteIndex = OutputBitsByteIndex + 1;
      while (byteIndex < uTXO.Length)
      {
        if (uTXO[byteIndex ++] != 0xFF)
        {
          return false;
        }
      }

      return true;
    }

    static bool TryIncrementCollisionBits(byte[] uTXO)
    {
      uint countCollisions = 
        (uint)((uTXO[CollisionBitsByteIndex] << CountTrailingBitsOfCollisionBitsInByte) 
        >> CountNonCollisionBitsInByte);

      if (CountCollisionsMax > countCollisions)
      {
        SetCountCollisions(uTXO, ++countCollisions);
        return true;
      }

      return false;
    }
    static bool TryIncrementCollisionBits(ref uint uTXO)
    {
      uint countCollisions = (uTXO << CountOutputAndExcessBits) >> CountNonCollisionBits;
      if (CountCollisionsMax > countCollisions)
      {
        SetCountCollisions(ref uTXO, ++countCollisions);
        return true;
      }

      return false;
    }

    static string Bytes2HexStringReversed(byte[] bytes)
    {
      var bytesReversed = new byte[bytes.Length];
      bytes.CopyTo(bytesReversed, 0);
      Array.Reverse(bytesReversed);
      return new SoapHexBinary(bytesReversed).ToString();
    }

    public void WriteUTXO(byte[] key, UInt256 headerHash, int outputsCount)
    {
      var lengthUTXOBits = 
        COUNT_HEADERINDEX_BITS + 
        COUNT_COLLISION_BITS + 
        outputsCount;

      byte[] headerHashBytes = headerHash.GetBytes();
      int primaryKey = BitConverter.ToInt32(key, 0);

      if (lengthUTXOBits <= COUNT_INTEGER_BITS)
      {
        uint uTXO = 0;

        for(int i = CountHeaderBytes; i > 0; i--)
        {
          uTXO <<= 8;
          uTXO |= headerHashBytes[i - 1];
        }
        uTXO <<= CountNonHeaderBits;
        uTXO >>= CountNonHeaderBits;

        if(lengthUTXOBits < COUNT_INTEGER_BITS)
        {
          uint maskSpendExcessOutputBits = uint.MaxValue << lengthUTXOBits;
          uTXO |= maskSpendExcessOutputBits;
        }

        if (PrimaryCacheCompressed.TryGetValue(primaryKey, out uint uTXOExisting))
        {
          if(TryIncrementCollisionBits(ref uTXOExisting))
          {
            PrimaryCacheCompressed[primaryKey] = uTXOExisting;
          }

          SecondaryCacheCompressed.Add(key, uTXO);
          //Archiver.WriteUTXO(key, uTXO);
        }
        else
        {
          PrimaryCacheCompressed.Add(primaryKey, uTXO);
          //Archiver.WriteUTXO(primaryKey, uTXOCompressedToInt);
        }
      }
      else
      {
        byte[] uTXO = new byte[(lengthUTXOBits + 7) / 8];

        Array.Copy(headerHashBytes, uTXO, CountHeaderBytes);
        uTXO[CountHeaderBytes - 1] &= MaskHeaderTailBitsInByte;

        var numberOfUTXOTailBits = lengthUTXOBits % 8;
        if (numberOfUTXOTailBits > 0)
        {
          uTXO[uTXO.Length - 1] |= (byte)(byte.MaxValue << numberOfUTXOTailBits);
        }

        if (PrimaryCache.TryGetValue(primaryKey, out byte[] uTXOExisting))
        {
          SecondaryCache.Add(key, uTXO);
          if (TryIncrementCollisionBits(uTXOExisting))
          {
            PrimaryCache[primaryKey] = uTXOExisting;
          }
          //Archiver.WriteUTXO(key, uTXO);
        }
        else
        {
          PrimaryCache.Add(primaryKey, uTXO);
          //Archiver.WriteUTXO(primaryKey, uTXOCompressedToInt);
        }
      }
    }
    static byte[] CreateUTXO(UInt256 headerHash, int outputsCount)
    {
      var lengthUTXOBits = COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS + outputsCount;
      byte[] uTXOIndex;
      if (lengthUTXOBits <= COUNT_INTEGER_BITS)
      {
        uTXOIndex = new byte[sizeof(uint)];
      }
      else
      {
        uTXOIndex = new byte[(lengthUTXOBits + 7) / 8];
      }

      var lengthHeaderIndexBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;
      Array.Copy(headerHash.GetBytes(), uTXOIndex, lengthHeaderIndexBytes);
                 
      var numberOfHeaderTailBits = lengthHeaderIndexBytes % 8;
      if (numberOfHeaderTailBits > 0)
      {
        for (int i = numberOfHeaderTailBits; i < 8; i++)
        {
          uTXOIndex[lengthHeaderIndexBytes] &= (byte)~(1 << i);
        }
      }


      return uTXOIndex;
    }
  }
}