using System;
using System.Security.Cryptography;



namespace BToken.Chaining
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

    const double MAX_TARGET = 2.695994666715064E67;
    public double Difficulty;



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

      Difficulty = MAX_TARGET /
        (double)UInt256.ParseFromCompact(nBits);
    }

    public byte[] GetBytes()
    {
      byte[] headerSerialized = 
        new byte[COUNT_HEADER_BYTES];

      BitConverter.GetBytes(Version)
        .CopyTo(headerSerialized, 0);

      HashPrevious.CopyTo(headerSerialized, 4);

      MerkleRoot.CopyTo(headerSerialized, 36);

      BitConverter.GetBytes(UnixTimeSeconds)
        .CopyTo(headerSerialized, 68);

      BitConverter.GetBytes(NBits)
        .CopyTo(headerSerialized, 72);

      BitConverter.GetBytes(Nonce)
        .CopyTo(headerSerialized, 76);

      return headerSerialized;
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

      uint unixTimeSeconds = BitConverter.ToUInt32(
        buffer, index);
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
