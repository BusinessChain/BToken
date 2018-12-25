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
      Headerchain.HeaderStream HeaderStream;

      public ChainLocation Location { get; private set; }


      public BlockStream(Blockchain blockchain)
      {
        Blockchain = blockchain;
        HeaderStream = Blockchain.Headers.GetHeaderStreamer();
      }

      public async Task<NetworkBlock> ReadBlockAsync()
      {
        Location = HeaderStream.ReadHeaderLocationTowardGenesis();

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
