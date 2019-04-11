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
      ChainHeader Header;
      uint Height;
      UInt256 Hash;


      public HeaderStream(Headerchain headerchain)
      {
        Header = headerchain.GenesisHeader;
        Height = 0;

        if (!TryGetHeaderHash(Header, out Hash))
        {
          Hash = Header.NetworkHeader.ComputeHash();
        }

      }
      public HeaderLocation GetHeaderLocation()
      {
        return GetHeaderLocations(1)[0];
      }
      public HeaderLocation[] GetHeaderLocations(int batchSize)
      {
        if (Header == null)
        {
          return null;
        }

        var headerLocations = new HeaderLocation[batchSize];

        for (int i = 0; i < batchSize; i++)
        {
          headerLocations[i] = new HeaderLocation(Height, Hash);

          Header = Header.HeadersNext == null ? null : Header.HeadersNext[0];

          if (!TryGetHeaderHash(Header, out Hash))
          {
            Hash = Header.NetworkHeader.ComputeHash();
          }

          Height++;
        }

        return headerLocations;
      }
    }
  }
}
