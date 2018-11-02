using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class ChainProbe
    {
      public Chain Chain;

      public ChainBlock Block;
      public UInt256 Hash;
      public double AccumulatedDifficulty;
      public uint Depth;


      public ChainProbe(Chain chain)
      {
        Chain = chain;

        Initialize();
      }

      public void Initialize()
      {
        Block = Chain.Socket.BlockTip;
        Hash = Chain.Socket.BlockTipHash;
        AccumulatedDifficulty = Chain.Socket.AccumulatedDifficulty;
        Depth = 0;
      }

      public bool GotoBlock(UInt256 hash)
      {
        Initialize();

        while (true)
        {
          if (IsHash(hash))
          {
            return true;
          }

          if (IsGenesis())
          {
            return false;
          }

          Push();
        }
      }

      public void Push()
      {
        Hash = Block.Header.HashPrevious;
        Block = Block.BlockPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

        Depth++;
      }

      public void InsertBlock(ChainBlock block, UInt256 headerHash)
      {
        ValidateUniqueness(headerHash);
        ValidateProofOfWork(block.Header.NBits, headerHash);
        ValidateTimeStamp(block.Header.UnixTimeSeconds);

        ConnectChainBlock(block);

        if (IsTip())
        {
          Chain.Socket.ExtendChain(block, headerHash);
        }
        else
        {
          ForkChain(block, headerHash);
        }
      }
      uint GetMedianTimePast()
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();
        ChainBlock block = Block;

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(block.Header.UnixTimeSeconds);

          if (block.BlockPrevious == null)
          { break; }

          block = block.BlockPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }
      void ValidateProofOfWork(uint nBits, UInt256 headerHash)
      {
        if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(nBits)))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }

        if (nBits != TargetManager.GetNextTargetBits(this))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void ValidateTimeStamp(uint unixTimeSeconds)
      {
        if (IsTimestampPremature(unixTimeSeconds))
        {
          throw new BlockchainException(BlockCode.PREMATURE);
        }

        if (unixTimeSeconds <= GetMedianTimePast())
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void ValidateUniqueness(UInt256 hash)
      {
        if (Block.BlocksNext.Any(b => Chain.GetHeaderHash(b).IsEqual(hash)))
        {
          throw new BlockchainException(BlockCode.DUPLICATE);
        }
      }
      bool IsTimestampPremature(ulong unixTimeSeconds)
      {
        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
      }
      void ConnectChainBlock(ChainBlock block)
      {
        block.BlockPrevious = Block;
        Block.BlocksNext.Add(block);
      }

      public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
      public bool IsTip() => Block == Chain.Socket.BlockTip;
      public bool IsGenesis() => Block == Chain.Socket.BlockGenesis;
      public uint GetHeight() => Chain.GetHeight() - Depth;

    }
  }
}
