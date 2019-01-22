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
      public class HeaderReader : ChainProbe
      {
        ChainHeader GenesisHeader;


        public HeaderReader(Headerchain headerchain)
          :base(headerchain.MainChain)
        {
          GenesisHeader = headerchain.GenesisHeader;
        }
               
        public ChainLocation ReadHeaderLocation()
        {
          if (Header != GenesisHeader)
          {
            var chainLocation = new ChainLocation(GetHeight(), Hash);
            Push();

            return chainLocation;
          }

          return null;
        }

        public NetworkHeader ReadHeader(out UInt256 headerHash)
        {
          if(Header == null)
          {
            headerHash = null;
            return null;
          }

          NetworkHeader header = Header.NetworkHeader;
          headerHash = Hash;

          if (Header == GenesisHeader)
          {
            Header = null;
          }
          else
          {
            Push();
          }

          return header;
        }

      }
    }
  }
}
