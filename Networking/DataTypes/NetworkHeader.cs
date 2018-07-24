using System;
using System.Collections.Generic;

namespace BToken
{
  class NetworkHeader
  {
    public const int HEADER_LENGTH = 81;

    public UInt32 Version { get; private set; }
    public UInt256 HashPrevious { get; private set; }
    public UInt256 MerkleRootHash { get; private set; }
    public UInt32 UnixTimeSeconds { get; private set; }
    public UInt32 NBits { get; private set; }
    public UInt32 Nonce { get; private set; }
    public Byte TXnCount { get; private set; }


    public NetworkHeader(
      UInt32 version, 
      UInt256 hashPrevious,
      UInt256 merkleRootHash,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce,
      Byte txnCount)
    {
      Version = version;
      HashPrevious = hashPrevious;
      MerkleRootHash = merkleRootHash;
      UnixTimeSeconds = unixTimeSeconds;
      NBits = nBits;
      Nonce = nonce;
      TXnCount = txnCount;
    }

    public static NetworkHeader deserialize(byte[] header)
    {
      int startIndex = 0;
      byte[] tempByteArray = new byte[UInt256.BYTE_LENGTH];

      UInt32 version = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      Array.Copy(header, startIndex, tempByteArray, 0, UInt256.BYTE_LENGTH);
      UInt256 previousHeaderHash = new UInt256(tempByteArray);
      startIndex += UInt256.BYTE_LENGTH;

      Array.Copy(header, startIndex, tempByteArray, 0, UInt256.BYTE_LENGTH);
      UInt256 merkleRootHash = new UInt256(tempByteArray);
      startIndex += UInt256.BYTE_LENGTH;

      UInt32 unixTimeSeconds = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      UInt32 nBits = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      UInt32 nonce = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      Byte txnCount = header[startIndex];
      startIndex += 1;

      return new NetworkHeader(version, previousHeaderHash, merkleRootHash, unixTimeSeconds, nBits, nonce, txnCount);
    }

    public byte[] getBytes()
    {
      List<byte> headerSerialized = new List<byte>();

      headerSerialized.AddRange(BitConverter.GetBytes(Version));
      headerSerialized.AddRange(HashPrevious.getBytes());
      headerSerialized.AddRange(MerkleRootHash.getBytes());
      headerSerialized.AddRange(BitConverter.GetBytes(UnixTimeSeconds));
      headerSerialized.AddRange(BitConverter.GetBytes(NBits));
      headerSerialized.AddRange(BitConverter.GetBytes(Nonce));

      return headerSerialized.ToArray();
    }

  }
}
