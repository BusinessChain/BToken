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
    public class HeaderStream
    {
      ChainHeader GenesisHeader;
      ChainProbe Probe;


      public HeaderStream(Headerchain headerchain)
      {
        GenesisHeader = headerchain.GenesisHeader;
        Probe = new ChainProbe(headerchain.MainChain);
      }

      public NetworkHeader ReadHeader(out ChainLocation chainLocation)
      {
        if (Probe.Header != GenesisHeader)
        {
          chainLocation = new ChainLocation(Probe.GetHeight(), Probe.Hash);
          NetworkHeader header = Probe.Header.NetworkHeader;

          Probe.Push();

          return header;
        }

        chainLocation = null;
        return null;
      }

    }
  }
}
