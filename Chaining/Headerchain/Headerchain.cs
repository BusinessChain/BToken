using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain : Chain
  {
    partial class Headerchain : Chain
    {
      Network Network;
      UInt256 CheckpointHash;


      public Headerchain(ChainHeader genesisHeader, UInt256 checkpointHash, Network network)
        : base(genesisHeader)
      {
        CheckpointHash = checkpointHash;
        Network = network;
      }

      public async Task buildAsync()
      {
        //List<UInt256> headerLocator = getHeaderLocator();
        //BufferBlock<NetworkHeader> networkHeaderBuffer = new BufferBlock<NetworkHeader>();
        //Network.GetHeadersAsync(headerLocator);
        //await insertNetworkHeadersAsync(networkHeaderBuffer);
      }

      public List<UInt256> getHeaderLocator()
      {
        uint getNextLocation(uint locator)
        {
          if (locator < 10)
            return locator + 1;
          else
            return locator * 2;
        }

        return getChainLinkLocator(CheckpointHash, getNextLocation);
      }

      public ChainHeader GetChainHeader(UInt256 hash)
      {
        return (ChainHeader)GetChainLink(hash);
      }

      public async Task insertNetworkHeadersAsync(BufferBlock<NetworkHeader> headerBuffer)
      {
        NetworkHeader networkHeader = await headerBuffer.ReceiveAsync();

        while (networkHeader != null)
        {
          insertNetworkHeader(networkHeader);

          networkHeader = await headerBuffer.ReceiveAsync();
        }
      }
      public void insertNetworkHeader(NetworkHeader networkHeader)
      {
        UInt256 hash = calculateHash(networkHeader.getBytes());

        ChainHeader chainHeader = new ChainHeader(
          hash,
          networkHeader.HashPrevious,
          networkHeader.NBits,
          networkHeader.MerkleRootHash,
          networkHeader.UnixTimeSeconds
          );

        insertChainLink(chainHeader);
      }
      static UInt256 calculateHash(byte[] headerBytes)
      {
        byte[] hashBytes = Hashing.sha256d(headerBytes);
        return new UInt256(hashBytes);
      }
      
      public override double GetDifficulty(ChainLink chainLink)
      {
        ChainHeader chainHeader = (ChainHeader)chainLink;
        return TargetManager.getDifficulty(chainHeader.NBits);
      }


      protected override void Validate(ChainLink chainLinkPrevious, ChainLink chainLink)
      {
        base.Validate(chainLinkPrevious, chainLink);

        ChainHeader header = (ChainHeader)chainLink;

        if (header.Hash.isGreaterThan(TargetManager.getNextTarget()))
        {
          throw new ChainLinkException(header, ChainLinkCode.INVALID);
        }

        if (IsChainLinkDeeperThanCheckpoint(chainLinkPrevious, CheckpointHash))
        {
          throw new ChainLinkException(header, ChainLinkCode.CHECKPOINT);
        }

        if (header.UnixTimeSeconds <= getMedianTimePast())
        {
          throw new ChainLinkException(header, ChainLinkCode.INVALID);
        }

        if (isTimeTwoHoursPastLocalTime())
        {
          throw new ChainLinkException(header, ChainLinkCode.EXPIRED);
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
}
