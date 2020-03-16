using System;
using System.Collections.Generic;
using System.Security.Cryptography;



namespace BToken
{
  public class Header
  {
    public Header HeaderPrevious;
    public Header HeaderNext;
    
    const int COUNT_HEADER_BYTES = 80;

    public byte[] Hash;
    public uint Version;
    public byte[] HashPrevious;
    public byte[] MerkleRoot;
    public uint UnixTimeSeconds;
    public uint NBits;
    public uint Nonce;


    public Header(
      byte[] headerHash,
      uint version,
      byte[] hashPrevious,
      byte[] merkleRootHash,
      uint unixTimeSeconds,
      uint nBits,
      uint nonce)
    {
      Hash = headerHash;
      Version = version;
      HashPrevious = hashPrevious;
      MerkleRoot = merkleRootHash;
      UnixTimeSeconds = unixTimeSeconds;
      NBits = nBits;
      Nonce = nonce;
    }

    public byte[] GetBytes()
    {
      List<byte> headerSerialized = new List<byte>();

      headerSerialized.AddRange(BitConverter.GetBytes(Version));
      headerSerialized.AddRange(HashPrevious);
      headerSerialized.AddRange(MerkleRoot);
      headerSerialized.AddRange(BitConverter.GetBytes(UnixTimeSeconds));
      headerSerialized.AddRange(BitConverter.GetBytes(NBits));
      headerSerialized.AddRange(BitConverter.GetBytes(Nonce));

      return headerSerialized.ToArray();
    }

    public static Header ParseHeader(
      byte[] buffer, 
      ref int index, 
      SHA256 sHA256)
    {
      byte[] hash =
        sHA256.ComputeHash(
          sHA256.ComputeHash(
            buffer,
            index,
            COUNT_HEADER_BYTES));

      uint version = BitConverter.ToUInt32(buffer, index);
      index += 4;

      byte[] previousHeaderHash = new byte[32];
      Array.Copy(buffer, index, previousHeaderHash, 0, 32);
      index += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, index, merkleRootHash, 0, 32);
      index += 32;

      uint unixTimeSeconds = BitConverter.ToUInt32(buffer, index);
      index += 4;
      
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      if (unixTimeSeconds >
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 
        MAX_FUTURE_TIME_SECONDS))
      {
        throw new ChainException(
          string.Format("Timestamp premature {0}",
            new DateTime(unixTimeSeconds).Date));
      }

      uint nBits = BitConverter.ToUInt32(buffer, index);
      index += 4;

      if (hash.IsGreaterThan(nBits))
      {
        throw new ChainException(
          string.Format("header hash {0} greater than NBits {1}",
            hash.ToHexString(),
            nBits));
      }

      uint nonce = BitConverter.ToUInt32(buffer, index);
      index += 4;

      return new Header(
        hash,
        version, 
        previousHeaderHash, 
        merkleRootHash, 
        unixTimeSeconds, 
        nBits, 
        nonce);
    }
  }

}
