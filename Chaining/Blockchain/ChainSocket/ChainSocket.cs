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
      ChainBlock BlockNoPayloadDeepest;
      ChainBlock BlockGenesis;
      public UInt256 HashBlockTip { get; private set; }

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
          BlockNoPayloadDeepest = block;
        }
        
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip = height;

        HeaderProbe = new SocketProbeHeader(this);
        PayloadProbe = new SocketProbePayload(this);
      }

      public bool InsertBlockPayload(IBlockPayload payload, UInt256 headerHash)
      {
        PayloadProbe.GoToBlock(BlockNoPayloadDeepest);

        while (true)
        {
          if (PayloadProbe.IsHash(headerHash))
          {
            PayloadProbe.InsertPayload(payload);

            if(PayloadProbe.IsBlockNoPayloadDeepest())
            {
              BlockNoPayloadDeepest = GetNextUpperBlockNoPayload();
            }

            return true;
          }

          if (PayloadProbe.IsTip())
          {
            return false;
          }

          PayloadProbe.Pull();
        }
      }
      ChainBlock GetNextUpperBlockNoPayload()
      {
        while(true)
        {
          if(PayloadProbe.IsTip())
          {
            return null;
          }

          PayloadProbe.Pull();

          if(!PayloadProbe.IsPayloadAssigned())
          {
            return PayloadProbe.Block;
          }
        }
      }

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

        if(BlockNoPayloadDeepest == null && !block.IsPayloadAssigned())
        {
          BlockNoPayloadDeepest = block;
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
