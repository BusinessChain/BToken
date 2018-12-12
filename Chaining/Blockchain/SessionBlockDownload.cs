using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class SessionBlockDownload : INetworkSession
    {
      INetworkChannel Channel;
      BlockArchiver Archiver;
      
      ChainLocation HeaderLocation;

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(ChainLocation headerLocation, BlockArchiver archiver)
      {
        HeaderLocation = headerLocation;
        Archiver = archiver;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        await DownloadBlockAsync();

        Console.WriteLine("Thread '{0}'> '{1}' downloaded block height: '{2}'",
          Thread.CurrentThread.ManagedThreadId,
          Channel.GetHashCode(),
          HeaderLocation.Height);
      }
      
      async Task DownloadBlockAsync()
      {
        try
        {
          NetworkBlock block = await GetBlockAsync(HeaderLocation.Hash);
          await Archiver.ArchiveBlockAsync(block, HeaderLocation.Hash);
        }
        catch(Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
      public async Task<NetworkBlock> GetBlockAsync(UInt256 hash)
      {
        var inventory = new Inventory(InventoryType.MSG_BLOCK, hash);
        await Channel.SendMessageAsync(new GetDataMessage(new List<Inventory>() { inventory }));

        var CancellationGetBlock = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

        while (true)
        {
          NetworkMessage networkMessage = await Channel.ReceiveMessageAsync(CancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            return new BlockMessage(networkMessage).NetworkBlock;
          }
        }
      }

    }
  }
}
