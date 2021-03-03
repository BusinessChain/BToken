using System;
using System.Security.Cryptography;



namespace BToken.Chaining
{
  public class Header
  {
    public Header HeaderPrevious;
    public Header HeaderNext;
    
    public const int COUNT_HEADER_BYTES = 80;

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
  }
}
