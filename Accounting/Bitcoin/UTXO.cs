using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    INetwork Network;
    Blockchain Blockchain;


    // API
    public UTXO(Blockchain blockchain, INetwork network)
    {
      Network = network;
      Blockchain = blockchain;

    }


    public async Task StartAsync()
    {
      // Load from UTXO archive

      try
      {
        Blockchain.BlockStream blockStream = Blockchain.GetBlockStream();
        NetworkBlock block = await blockStream.ReadBlockAsync().ConfigureAwait(false);

        while (block != null)
        {
          InsertBlock(block);
          block = await blockStream.ReadBlockAsync().ConfigureAwait(false);
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

      Console.WriteLine("UTXO syncing completed");

      // Listener to new blocks.
    }

    void InsertBlock(NetworkBlock block)
    {
      // erstelle UTXO STXO
      Console.WriteLine("Thread: '{0}'> UTXO processes block: " + block.Header.HashPrevious, Thread.CurrentThread.ManagedThreadId);

    }

  }
}
