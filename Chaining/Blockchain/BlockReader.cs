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
    public class BlockReader
    {
      Blockchain Blockchain;
      Headerchain.HeaderReader HeaderReader;

      public ChainLocation Location { get; private set; }


      public BlockReader(Blockchain blockchain)
      {
        Blockchain = blockchain;
        HeaderReader = Blockchain.Headers.GetHeaderReader();
      }

      public async Task<NetworkBlock> ReadBlockNextInChainAsync()
      {
        Location = HeaderReader.ReadHeaderLocationTowardGenesis();

        if (Location == null) { return null; }

        while (true)
        {
          try
          {
            return await Blockchain.Archiver.ReadBlockAsync(Location.Hash);
          }
          catch (IOException)
          {
            await Task.Delay(1000).ConfigureAwait(false);
            Console.WriteLine("waiting for Block '{0}' to download", Location.Hash);
          }
        }
      }
    }
  }
}
