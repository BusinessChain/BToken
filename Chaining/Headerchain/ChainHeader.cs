using System;
using System.Collections.Generic;
using System.Linq;


namespace BToken.Chaining
{
  partial class ChainHeader : ChainLink
  {
    public UInt64 UnixTimeSeconds { get; private set; }
    public UInt256 MerkleRootHash { get; private set; }

    public UInt256 Target { get; private set; }
    public double Difficulty { get; private set; }
    double AccumulatedDifficulty;


    public ChainHeader(
      UInt256 hash,
      UInt256 hashPrevious,
      UInt256 target,
      double difficulty,
      double accumulatedDifficulty,
      UInt256 merkleRootHash,
      UInt64 unixTimeSeconds)
      : base
      (
        hash: hash,
        hashPrevious: hashPrevious
      )
    {
      UnixTimeSeconds = unixTimeSeconds;
      MerkleRootHash = merkleRootHash;
      Target = target;
      Difficulty = difficulty;
      AccumulatedDifficulty = accumulatedDifficulty;
    }

    public ChainHeader(NetworkHeader networkHeader)
      : base
      (
          hash: calculateHash(networkHeader.getBytes()),
          hashPrevious: networkHeader.HashPrevious
      )
    {
      UnixTimeSeconds = networkHeader.UnixTimeSeconds;
      MerkleRootHash = networkHeader.MerkleRootHash;
    }
    static UInt256 calculateHash(byte[] headerBytes)
    {
      byte[] hashBytes = Hashing.sha256d(headerBytes);
      return new UInt256(headerBytes);
    }

    public override void connectToPrevious(ChainLink chainLinkPrevious)
    {
      base.connectToPrevious(chainLinkPrevious);

      ChainHeader headerPrevious = (ChainHeader)chainLinkPrevious;

      Target = TargetManager.getNextTarget(headerPrevious);
      Difficulty = TargetManager.getDifficulty(Target);
      AccumulatedDifficulty = headerPrevious.getAccumulatedDifficulty() + Difficulty;
    }

    public ChainHeader GetNextHeader(UInt256 hash)
    {
      return (ChainHeader)GetNextChainLink(hash);
    }
    public ChainHeader getHeaderPrevious()
    {
      return getHeaderPrevious(0);
    }
    public ChainHeader getHeaderPrevious(uint depth)
    {
      return (ChainHeader)getChainLinkPrevious(depth);
    }

    public override double getAccumulatedDifficulty()
    {
      return AccumulatedDifficulty;
    }
    public override void validate()
    {
      if (Hash.isGreaterThan(Target))
      {
        throw new ChainLinkException(this, ChainLinkCode.INVALID);
      }

      if (UnixTimeSeconds <= getMedianTimePast())
      {
        throw new ChainLinkException(this, ChainLinkCode.INVALID);
      }

      if (isTimeTwoHoursPastLocalTime())
      {
        throw new ChainLinkException(this, ChainLinkCode.EXPIRED);
      }
    }
    ulong getMedianTimePast()
    {
      const int MEDIAN_TIME_PAST = 11;

      List<ulong> timestampsPast = new List<ulong>();
      ChainHeader header = getHeaderPrevious();

      int depth = 0;
      while (depth < MEDIAN_TIME_PAST)
      {
        timestampsPast.Add(header.UnixTimeSeconds);

        if (header.isGenesis())
        { break; }

        header = header.getHeaderPrevious();
        depth++;
      }

      timestampsPast.Sort();

      return timestampsPast[timestampsPast.Count / 2];
    }
    bool isTimeTwoHoursPastLocalTime()
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)UnixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

  }
}
