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

      public bool TryReadHeader(out NetworkHeader header, out HeaderLocation chainLocation)
      {
        if (Probe.Header != GenesisHeader)
        {
          chainLocation = new HeaderLocation(Probe.GetHeight(), Probe.Hash);
          header = Probe.Header.NetworkHeader;

          Probe.Push();

          return true;
        }

        chainLocation = null;
        header = null;
        return false;
      }

    }
  }
}
