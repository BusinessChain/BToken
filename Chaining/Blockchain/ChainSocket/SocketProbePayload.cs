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
        
        public bool InsertPayload(IBlockPayload payload, UInt256 headerHash)
        {
          GoToBlock(Socket.BlockUnassignedPayloadDeepest);

          while (true)
          {
            if (IsHash(headerHash))
            {
              Block.InsertPayload(payload);

              if (IsBlockNoPayloadDeepest())
              {
                Socket.BlockUnassignedPayloadDeepest = GetNextUpperBlockNoPayload();
              }

              return true;
            }

            if (IsTip())
            {
              return false;
            }

            Pull();
          }
        }
        void GoToBlock(ChainBlock block)
        {
          Block = block;
          Hash = GetHashBlock(block);
        }
        ChainBlock GetNextUpperBlockNoPayload()
        {
          while (true)
          {
            if (IsTip())
            {
              return null;
            }

            Pull();

            if (!IsPayloadAssigned())
            {
              return Block;
            }
          }
        }

        public bool IsHash(UInt256 hash) => Hash.isEqual(hash);
        public bool IsBlockNoPayloadDeepest() => Block == Socket.BlockUnassignedPayloadDeepest;
        public bool IsTip() => Block == Socket.BlockTip;
        public bool IsPayloadAssigned() => Block.IsPayloadAssigned();
      }
    }
  }
}
