using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class ChainProbe
      {
        public Chain Chain;

        public ChainHeader Header;
        public UInt256 Hash;
        public uint Depth;


        public ChainProbe(Chain chain)
        {
          Chain = chain;

          Initialize();
        }

        protected virtual void Initialize()
        {
          Header = Chain.HeaderTip;
          Hash = Chain.HeaderTipHash;
          Depth = 0;
        }

        protected bool GoTo(UInt256 hash, ChainHeader stopHeader)
        {
          Initialize();

          while (true)
          {
            if (Hash.IsEqual(hash))
            {
              return true;
            }
            if (stopHeader == Header)
            {
              return false;
            }

            Push();
          }
        }
        protected virtual void Push()
        {
          Hash = Header.NetworkHeader.HashPrevious;
          Header = Header.HeaderPrevious;

          Depth++;
        }

        protected UInt256 GetHeaderHash(ChainHeader header)
        {
          if (header.HeadersNext.Any())
          {
            return header.HeadersNext[0].NetworkHeader.HashPrevious;
          }
          else if (header == Chain.HeaderTip)
          {
            return Chain.HeaderTipHash;
          }
          else
          {
            return header.NetworkHeader.ComputeHeaderHash();
          }
        }

        protected bool IsTip() => Header == Chain.HeaderTip;
        protected bool IsRoot() => Header == Chain.HeaderRoot;
        protected uint GetHeight() => Chain.Height - Depth;
      }
    }
  }
}
