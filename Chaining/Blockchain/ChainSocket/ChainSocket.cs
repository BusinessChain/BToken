using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BToken.Networking;

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
      }

      public bool InsertBlock(IBlockPayload blockPayload, UInt256 headerHash)
      {
        ChainBlock chainBlock = FindBlock(headerHash, BlockUnassignedPayloadDeepest);
        if (chainBlock == null) { return false; }

        chainBlock.InsertPayload(blockPayload);

        UpdateBlockUnassignedPayloadDeepest();

        return true;
      }
      void StoreToDisk(ChainBlock block)
      {

      }
      ChainBlock FindBlock(UInt256 headerHash, ChainBlock startBlock)
      {
        if (startBlock == null) { return null; }

        ChainBlock block = startBlock;

        while (!GetHash(block).IsEqual(headerHash))
        {
          if (block == BlockTip)
          {
            return null;
          }

          block = block.BlocksNext[0];
        }

        return block;
      }
      void UpdateBlockUnassignedPayloadDeepest()
      {
        ChainBlock block = BlockUnassignedPayloadDeepest;

        while (block.IsPayloadAssigned())
        {
          if (block == BlockTip)
          {
            BlockUnassignedPayloadDeepest = null;
          }

          block = block.BlocksNext[0];
        }

        BlockUnassignedPayloadDeepest = block;
      }

      public List<UInt256> GetLocatorBatchBlocksUnassignedPayload(int batchSize)
      {
        ChainBlock startBlock = BlockUnassignedPayloadDeepest;

        if (startBlock == null) { return new List<UInt256>(); }
        
        var locatorBatchBlocksUnassignedPayload = new List<UInt256>();
        while (locatorBatchBlocksUnassignedPayload.Count < batchSize)
        {
          if (!startBlock.IsPayloadAssigned())
          {
            locatorBatchBlocksUnassignedPayload.Add(GetHash(startBlock));
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
      
      UInt256 GetHash(ChainBlock block)
      {
        if(block == BlockTip)
        {
          return HashBlockTip;
        }

        return block.BlocksNext[0].Header.HashPrevious;
      }
    }
  }
}
