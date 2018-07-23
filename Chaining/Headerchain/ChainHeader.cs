using System;
using System.Collections.Generic;
using System.Linq;


namespace BToken.Chaining
{
  partial class ChainHeader : ChainLink
  {
    NetworkHeader NetworkHeader;

    UInt256 Hash;
    uint Height;
    public UInt256 Target { get; private set; }
    public double Difficulty { get; private set; }
    double AccumulatedDifficulty;
    public ulong MedianTimePast { get; private set; }


    public ChainHeader(
      UInt256 headerHash,
      ChainHeader previousHeader,
      uint height,
      UInt256 target,
      double difficulty,
      double accumulatedDifficulty,
      ulong medianTimePast)
    {
      Hash = headerHash;
      Height = height;
      Target = target;
      Difficulty = difficulty;
      AccumulatedDifficulty = accumulatedDifficulty;
      MedianTimePast = medianTimePast;
    }

    public ChainHeader(NetworkHeader networkHeader)
    {
      NetworkHeader = networkHeader;

      Hash = calculateHeaderHash(networkHeader.getBytes());
    }
    static UInt256 calculateHeaderHash(byte[] headerBytes)
    {
      byte[] hashBytes = Hashing.sha256d(headerBytes);
      return new UInt256(headerBytes);
    }

    public override void connectToPrevious(ChainLink chainLinkPrevious)
    {
      base.connectToPrevious(chainLinkPrevious);

      ChainHeader headerPrevious = (ChainHeader)chainLinkPrevious;

      Height = headerPrevious.getHeight() + 1;
      Target = TargetManager.getNextTarget(headerPrevious);
      Difficulty = TargetManager.getDifficulty(Target);
      AccumulatedDifficulty = headerPrevious.getAccumulatedDifficulty() + Difficulty;
      MedianTimePast = getMedianTimePast(headerPrevious);
    }
    static ulong getMedianTimePast(ChainHeader header)
    {
      const int MEDIAN_TIME_PAST = 11;
      List<ulong> timestampsPast = new List<ulong>();

      int depth = 0;
      while (depth < MEDIAN_TIME_PAST)
      {
        timestampsPast.Add(header.getUnixTimeSeconds());

        if (header.isGenesis())
        { break; }

        header = header.getHeaderPrevious();
        depth++;
      }

      timestampsPast.Sort();

      return timestampsPast[timestampsPast.Count / 2];
    }

    public ChainHeader GetNextHeader(UInt256 hash)
    {
      return (ChainHeader)GetChainLink(hash);
    }
    public ChainHeader getHeaderPrevious()
    {
      return getHeaderPrevious(0);
    }
    public ChainHeader getHeaderPrevious(uint depth)
    {
      return (ChainHeader)getChainLinkPrevious(depth);
    }
    public UInt256 getMerkleRootHash()
    {
      return NetworkHeader.MerkleRootHash;
    }
    public UInt64 getUnixTimeSeconds()
    {
      return NetworkHeader.UnixTimeSeconds;
    }

    public override UInt256 getHashPrevious()
    {
      return NetworkHeader.PreviousHeaderHash;
    }
    public override UInt256 getHash()
    {
      return Hash;
    }
    public override uint getHeight()
    {
      return Height;
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

      if (getUnixTimeSeconds() <= MedianTimePast)
      {
        throw new ChainLinkException(this, ChainLinkCode.INVALID);
      }

      if (isTimeTwoHoursPastLocalTime(getUnixTimeSeconds()))
      {
        throw new ChainLinkException(this, ChainLinkCode.EXPIRED);
      }
    }
    static bool isTimeTwoHoursPastLocalTime(ulong time)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)time > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

  }
}
