using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using BToken.Hashing;

namespace BToken.Networking
{
  public class NetworkHeader
  {
    public UInt32 Version { get; private set; }
    public byte[] HashPrevious { get; private set; }
    public byte[] MerkleRoot { get; private set; }
    public UInt32 UnixTimeSeconds { get; private set; }
    public UInt32 NBits { get; private set; }
    public UInt32 Nonce { get; private set; }


    public NetworkHeader(
      UInt32 version, 
      byte[] hashPrevious,
      byte[] merkleRootHash,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce)
    {
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

    public static NetworkHeader ParseHeader(byte[] buffer, out int txCount, ref int startIndex)
    {
      UInt32 version = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      byte[] previousHeaderHash = new byte[32];
      Array.Copy(buffer, startIndex, previousHeaderHash, 0, 32);
      startIndex += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, startIndex, merkleRootHash, 0, 32);
      startIndex += 32;

      UInt32 unixTimeSeconds = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      UInt32 nBits = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      UInt32 nonce = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      txCount = (int)VarInt.GetUInt64(buffer, ref startIndex);

      return new NetworkHeader(
        version, 
        previousHeaderHash, 
        merkleRootHash, 
        unixTimeSeconds, 
        nBits, 
        nonce);
    }
  }

  public static class NetworkHeaderExtensionMethods
  {
    public static byte[] ComputeHash(
      this NetworkHeader header)
    {
      return SHA256d.Compute(header.GetBytes());
    }
  }
}
