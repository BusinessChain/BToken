using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public class BlockStream
    {
      Blockchain Blockchain;
      Headerchain.HeaderReader HeaderReader;

      public ChainLocation Location { get; private set; }


      public BlockStream(Blockchain blockchain)
      {
        Blockchain = blockchain;
        HeaderReader = Blockchain.Headers.GetHeaderReader();
      }

      public async Task<NetworkBlock> ReadBlockAsync()
      {
        HeaderReader.ReadHeader(out ChainLocation location);
        Location = location;

        if (Location == null)
        {
          return null;
        }
        else
        {
          return await Blockchain.Archiver.ReadBlockAsync(Location.Hash);
        }
      }
    }
  }

}
