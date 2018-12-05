using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    class ChainInserter
    {
      ChainProbe Probe;
      
      public ChainInserter(ChainProbe chainProbe)
      {
        Probe = chainProbe;
      }
      
      public void ConnectHeader(NetworkHeader header)
      {
        var chainHeader = new ChainHeader(header, Probe.Header);
        Probe.Header.HeadersNext.Add(chainHeader);
      }
      public void ExtendChain(UInt256 headerHash)
      {
        ChainHeader block = Probe.Header.HeadersNext.Last();
        Probe.Chain.ExtendChain(block, headerHash);
      }
      public void ForkChain(UInt256 headerHash)
      {
        ChainHeader header = Probe.Header.HeadersNext.Last();
        uint headerTipHeight = Probe.GetHeight() + 1;

        Probe.Chain = new Chain(
          headerTip: header,
          headerTipHash: headerHash,
          headerTipHeight: headerTipHeight,
          headerRoot: header,
          accumulatedDifficultyPrevious: Probe.AccumulatedDifficulty);

      }
    }
  }
}
