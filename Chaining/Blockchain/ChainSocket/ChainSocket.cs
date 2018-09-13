using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class ChainSocket
    {
      Blockchain Blockchain;

      ChainBlock BlockTip;
      public UInt256 HashBlockTip { get; private set; }
      ChainBlock BlockUnassignedPayloadDeepest;
      ChainBlock BlockGenesis;

      double AccumulatedDifficulty;
      public uint HeightBlockTip { get; private set; }

      public ChainSocket StrongerSocket { get; private set; }
      public ChainSocket WeakerSocket { get; private set; }

      public SocketProbeHeader HeaderProbe { get; private set; }
      SocketProbePayload PayloadProbe;



      public ChainSocket
        (
        Blockchain blockchain,
        ChainBlock block,
        UInt256 hash,
        double accumulatedDifficultyPrevious,
        uint height
        )
      {
        Blockchain = blockchain;

        BlockGenesis = block;
        BlockTip = block;
        HashBlockTip = hash;

        if(!block.IsPayloadAssigned())
        {
          BlockUnassignedPayloadDeepest = block;
        }
        
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip = height;

        HeaderProbe = new SocketProbeHeader(this);
        PayloadProbe = new SocketProbePayload(this);
      }

      public bool InsertBlockPayload(IBlockPayload payload, UInt256 headerHash)
      {
        return PayloadProbe.InsertPayload(payload, headerHash);
      }

      public List<UInt256> GetLocatorBatchBlocksUnassignedPayload(int batchSize)
      {
        if (IsNoPayloadUnassigned())
        {
          return new List<UInt256>();
        }

        return GetLocatorBatchBlocksUnassignedPayload(batchSize, BlockUnassignedPayloadDeepest.Header.HashPrevious);
      }
      public List<UInt256> GetLocatorBatchBlocksUnassignedPayload(int batchSize, UInt256 locationStart)
      {
        if (IsNoPayloadUnassigned()) { return new List<UInt256>(); }

        ChainBlock startBlock = GoToLocationStartBlock(BlockUnassignedPayloadDeepest, locationStart);
        return CollectBlocksUnassignedPayload(startBlock, batchSize);
      }
      ChainBlock GoToLocationStartBlock(ChainBlock startBlock, UInt256 locationStart)
      {
        while (!startBlock.Header.HashPrevious.isEqual(locationStart))
        {
          if (startBlock == BlockTip)
          {
            return null;
          }

          startBlock = startBlock.BlocksNext[0];
        }

        return startBlock;
      }
      List<UInt256> CollectBlocksUnassignedPayload(ChainBlock startBlock, int batchSize)
      {
        var locatorBatchBlocksUnassignedPayload = new List<UInt256>();
        while (locatorBatchBlocksUnassignedPayload.Count < batchSize)
        {
          if (startBlock == null)
          {
            return locatorBatchBlocksUnassignedPayload;
          }

          if (!startBlock.IsPayloadAssigned())
          {
            locatorBatchBlocksUnassignedPayload.Add(GetHashBlock(startBlock));
          }

          if (startBlock == BlockTip)
          {
            return locatorBatchBlocksUnassignedPayload;
          }

          startBlock = startBlock.BlocksNext[0];
        }

        return locatorBatchBlocksUnassignedPayload;
      }
      bool IsNoPayloadUnassigned() => BlockUnassignedPayloadDeepest == null;

      public SocketProbeHeader GetProbeAtBlock(UInt256 hash)
      {
        HeaderProbe.Reset();

        while (true)
        {
          if (HeaderProbe.IsHash(hash))
          {
            return HeaderProbe;
          }

          if (HeaderProbe.IsGenesis())
          {
            return null;
          }

          HeaderProbe.Push();
        }
      }
      
      public void ConnectWeakerSocket(ChainSocket weakerSocket)
      {
        weakerSocket.WeakerSocket = WeakerSocket;
        weakerSocket.StrongerSocket = this;

        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = weakerSocket;
        }

        WeakerSocket = weakerSocket;
      }
      
      void ConnectNextBlock(ChainBlock block, UInt256 headerHash)
      {
        BlockTip = block;
        HashBlockTip = headerHash;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip++;

        if(BlockUnassignedPayloadDeepest == null && !block.IsPayloadAssigned())
        {
          BlockUnassignedPayloadDeepest = block;
        }
      }

      void Disconnect()
      {
        if (StrongerSocket != null)
        {
          StrongerSocket.WeakerSocket = WeakerSocket;
        }
        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = StrongerSocket;
        }

        StrongerSocket = null;
        WeakerSocket = null;
      }
      
      public bool IsStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return AccumulatedDifficulty > socket.AccumulatedDifficulty;
      }
      public bool IsProbeStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return HeaderProbe.IsStrongerThan(socket.HeaderProbe);
      }
      
      
    }
  }
}
