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
      public int Height { get; private set; }
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
        TryGetHeaderLocations(1, out HeaderLocation[] headerLocations);
        return headerLocations[0];
      }
      public bool TryGetHeaderLocations(int batchSize, out HeaderLocation[] headerLocations)
      {
        if (Header != null || batchSize > 0)
        {
          headerLocations = new HeaderLocation[batchSize];
          for (int i = 0; i < batchSize; i++)
          {
            headerLocations[i] = new HeaderLocation(Height, Hash);

            if (Header.HeadersNext == null)
            {
              Header = null;
              Array.Resize(ref headerLocations, batchSize + 1);
              break;
            }
            else
            {
              Header = Header.HeadersNext[0];
              if (!TryGetHeaderHash(Header, out Hash))
              {
                Hash = Header.NetworkHeader.ComputeHash();
              }
              Height++;
            }
          }

          return true;
        }

        headerLocations = null;
        return false;
      }
    }
  }
}
