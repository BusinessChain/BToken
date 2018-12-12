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


      public BlockStream(Blockchain blockchain)
      {
        Blockchain = blockchain;
        HeaderStream = Blockchain.Headers.GetHeaderStreamer();
      }

      public async Task<NetworkBlock> ReadBlockAsync()
      {
        ChainLocation location = HeaderStream.ReadHeaderLocationTowardGenesis();

        if (location == null) { return null; }
        return await RetrieveBlockFromArchive(location);
      }

      async Task<NetworkBlock> RetrieveBlockFromArchive(ChainLocation location)
      {
        while (true)
        {
          try
          {
            return await Blockchain.Archiver.ReadBlockAsync(location.Hash);
          }
          catch (IOException)
          {
            await Task.Delay(1000).ConfigureAwait(false);
            Console.WriteLine("waiting for Block '{0}' to download", location.Hash);
          }
        }
      }
    }
  }
}
