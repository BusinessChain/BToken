using System;
using System.Collections.Generic;

namespace BToken.Networking
{
  class NetworkHeader
  {
    public UInt32 Version { get; private set; }
    public UInt256 HashPrevious { get; private set; }
    public UInt256 MerkleRootHash { get; private set; }
    public UInt32 UnixTimeSeconds { get; private set; }
    public UInt32 NBits { get; private set; }
    public UInt32 Nonce { get; private set; }

    public int TxCount { get; private set; }


    public NetworkHeader(
      UInt32 version, 
      UInt256 hashPrevious,
      UInt256 merkleRootHash,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce,
      int txCount)
    {
      Version = version;
      HashPrevious = hashPrevious;
      MerkleRootHash = merkleRootHash;
      UnixTimeSeconds = unixTimeSeconds;
      NBits = nBits;
      Nonce = nonce;
      TxCount = txCount;
    }

    public byte[] getBytes()
    {
      List<byte> headerSerialized = new List<byte>();

      headerSerialized.AddRange(BitConverter.GetBytes(Version));
      headerSerialized.AddRange(HashPrevious.GetBytes());
      headerSerialized.AddRange(MerkleRootHash.GetBytes());
      headerSerialized.AddRange(BitConverter.GetBytes(UnixTimeSeconds));
      headerSerialized.AddRange(BitConverter.GetBytes(NBits));
      headerSerialized.AddRange(BitConverter.GetBytes(Nonce));

      return headerSerialized.ToArray();
    }

    public static NetworkHeader ParseHeader(byte[] byteStream, ref int startIndex)
    {
      UInt32 version = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      UInt256 previousHeaderHash = new UInt256(byteStream, ref startIndex);

      UInt256 merkleRootHash = new UInt256(byteStream, ref startIndex);

      UInt32 unixTimeSeconds = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      UInt32 nBits = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      UInt32 nonce = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      int txCount = (int)VarInt.getUInt64(byteStream, ref startIndex);

      return new NetworkHeader(
        version, 
        previousHeaderHash, 
        merkleRootHash, 
        unixTimeSeconds, 
        nBits, 
        nonce,
        txCount);
    }

  }
}
