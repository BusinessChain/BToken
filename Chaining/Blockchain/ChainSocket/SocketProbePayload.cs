using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      public class SocketProbePayload
      {
        ChainSocket Socket;
        
        public ChainBlock Block;
        public UInt256 Hash;


        public SocketProbePayload(ChainSocket socket)
        {
          Socket = socket;

          Block = Socket.BlockTip;
          Hash = Socket.HashBlockTip;
        }
        
        public void Pull()
        {
          Block = Block.BlocksNext.First();
          Hash = GetHashBlock(Block);
        }
        public void GoToBlock(ChainBlock block)
        {
          Block = block;
          Hash = GetHashBlock(block);
        }
        UInt256 GetHashBlock(ChainBlock block) => block == Socket.BlockTip ? Socket.HashBlockTip : block.BlocksNext[0].Header.HashPrevious;

        public void InsertPayload(IBlockPayload payload)
        {
          Block.InsertPayload(payload);
        }

        public bool IsHash(UInt256 hash) => Hash.isEqual(hash);
        public bool IsGenesis() => Block == Socket.BlockGenesis;
        public bool IsBlockNoPayloadDeepest() => Block == Socket.BlockNoPayloadDeepest;
        public bool IsTip() => Block == Socket.BlockTip;
        public bool IsPayloadAssigned() => Block.IsPayloadAssigned();
      }
    }
  }
}
