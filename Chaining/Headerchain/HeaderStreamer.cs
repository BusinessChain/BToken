using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    public class HeaderStreamer
    {
      Headerchain Headerchain;
      ChainProbe Probe;


      public HeaderStreamer(Headerchain headerchain)
      {
        Headerchain = headerchain;
        Probe = new ChainProbe(Headerchain.MainChain);
      }

      public ChainLocation ReadNextHeaderLocation()
      {
        if(Probe.GetHeight() > 0)
        {
          var chainLocation = new ChainLocation(Probe.GetHeight(), Probe.Hash);
          Probe.Push();

          return chainLocation;
        }

        return null;
      }
    }
  }
}
