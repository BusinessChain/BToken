using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    public class HeaderReader : ChainProbe
    {
      ChainHeader GenesisHeader;


      public HeaderReader(Headerchain headerchain)
        : base(headerchain.MainChain)
      {
        GenesisHeader = headerchain.GenesisHeader;
      }

      public NetworkHeader ReadHeader(out ChainLocation chainLocation)
      {
        if (Header != GenesisHeader)
        {
          chainLocation = new ChainLocation(GetHeight(), Hash);
          NetworkHeader header = Header.NetworkHeader;

          Push();

          return header;
        }

        chainLocation = null;
        return null;
      }

    }
  }
}
