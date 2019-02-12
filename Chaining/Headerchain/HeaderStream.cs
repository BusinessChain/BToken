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

      public NetworkHeader ReadHeader(out UInt256 hash, out uint height)
      {
        if (Probe.Header != GenesisHeader)
        {
          hash = Probe.Hash;
          height = Probe.GetHeight();
          NetworkHeader header = Probe.Header.NetworkHeader;

          Probe.Push();

          return header;
        }

        hash = null;
        height = 0;
        return null;
      }

    }
  }
}
