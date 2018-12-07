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
      public class HeaderStreamer : ChainProbe
      {
        Headerchain Headerchain;
        ChainProbe Probe;


        public HeaderStreamer(Headerchain headerchain)
        {
          Headerchain = headerchain;
          Probe = new ChainProbe(Headerchain.MainChain);
        }

        public bool GoTo(UInt256 hash)
        {
          Probe.Initialize();

          while (true)
          {
            if (Probe.Hash.IsEqual(hash))
            {
              return true;
            }

            if (Probe.Header == Probe.Chain.HeaderRoot)
            {
              return false;
            }

            Probe.Push();
          }
        }

        public ChainLocation ReadNextHeaderLocationTowardRoot()
        {
          if (Probe.Header != GenesisHeader)
          {
            var chainLocation = new ChainLocation(Probe.GetHeight(), Probe.Hash);
            Probe.Push();

            return chainLocation;
          }

          return null;
        }

        public ChainLocation ReadNextHeaderLocationTowardTip()
        {
          if (Probe.GetHeight() > 0)
          {
            var chainLocation = new ChainLocation(Probe.GetHeight(), Probe.Hash);
            Probe.Push();

            return chainLocation;
          }

          return null;
        }

        public NetworkHeader ReadNextHeader()
        {
          if (Probe.GetHeight() > 0)
          {
            var header = Probe.Header;
            Probe.Push();

            return header.NetworkHeader;
          }

          return null;
        }
      }
    }
  }
}
