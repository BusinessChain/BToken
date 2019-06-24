using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Chaining;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class NetworkUTXOBatchLoader
    {
      const int COUNT_DOWNLOAD_TASKS = 8;

      UTXO UTXO;
      public BufferBlock<UTXODownloadBatch> DownloadBuffer = new BufferBlock<UTXODownloadBatch>();
      public Dictionary<int, UTXODownloadBatch> DownloadQueue = new Dictionary<int, UTXODownloadBatch>();
      int IndexBatcher = 0;
      Queue<Block> FIFOBlocks = new Queue<Block>();
      int TXCountFIFO;
      readonly object LOCK_DownloadIndex = new object();
      int DownloadIndex;

      readonly object LOCK_HeaderHashSentLast = new object();
      byte[] HeaderHashSentToMergerLast = new byte[COUNT_HEADER_BYTES];



      public NetworkUTXOBatchLoader(UTXO uTXO)
      {
        UTXO = uTXO;
      }

      public async Task RunAsync(byte[] headerHashSentToMergerLast)
      {
        HeaderHashSentToMergerLast = headerHashSentToMergerLast;

        Task runBatcherTask = RunBatcherAsync();
        Task runTXParserTask = RunTXParser();

        for (int i = 0; i < COUNT_DOWNLOAD_TASKS; i += 1)
        {
          var sessionBlockDownload = new SessionBlockDownload(this);
          Task runDownloadTask = UTXO.Network.RunSessionAsync(sessionBlockDownload);
        }
        DownloadBuffer.
      }

      async Task RunBatcherAsync()
      {
        while(true)
        {
          UTXODownloadBatch downloadBatch = await DownloadBuffer.ReceiveAsync();

          if(downloadBatch.Index != IndexBatcher)
          {
            DownloadQueue.Add(downloadBatch.Index, downloadBatch);
            continue;
          }

          while (true)
          {
            foreach (Block block in downloadBatch.Blocks)
            {
              FIFOBlocks.Enqueue(block);
              TXCountFIFO += block.TXCount;
            }
          }
        }
      }
      async Task RunTXParser()
      {

      }
      
      bool TryGetDownloadBatch(
        out UTXODownloadBatch downloadBatch,
        int count,
        SHA256 sHA256)
      {
        downloadBatch = new UTXODownloadBatch
        {
          HeaderHashes = new byte[count][],
          Blocks = new List<Block>(count)
        };
        
        int i = 0;

        lock (LOCK_DownloadIndex)
        {
          downloadBatch.Index = DownloadIndex;
          DownloadIndex += 1;

          Headerchain.ChainHeader header
            = UTXO.Headerchain.ReadHeader(HeaderHashSentToMergerLast, sHA256);

          while (i < count && header.HeadersNext != null)
          {
            downloadBatch.HeaderHashes[i] = header.GetHeaderHash(sHA256);
            header = header.HeadersNext[0];
            i += 1;
          }
        }

        if (i > 0)
        {
          return true;
        }
        else
        {
          return false;
        }
      }
    }
  }
}
