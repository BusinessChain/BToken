using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net.Sockets;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class NetworkSynchronizer
    {
      Network Network;
      Blockchain Blockchain;

      int TIMEOUT_SYNCHRONIZER = 30000;

      const int UTXOIMAGE_INTERVAL_SYNC = 500;
      const int UTXOIMAGE_INTERVAL_LISTEN = 5;

      StreamWriter LogFile;
      


      public NetworkSynchronizer(Blockchain blockchain)
      {
        Blockchain = blockchain;
        Network = new Network(blockchain);
        
        LogFile = new StreamWriter("logSynchronizer", false);

        try
        {
          Directory.Delete("logPeers", true);
        }
        catch(Exception ex)
        {
          ex.Message.Log(LogFile);
        }
      }



      public void Start()
      {
        StartPeerSynchronizer();

        Network.Start();
      }

                                         
      
      async Task StartPeerSynchronizer()
      {
        "Start Peer synchronizer".Log(LogFile);
        
        while (true)
        {
          await Task.Delay(3000).ConfigureAwait(false);

          if (!Blockchain.TryLock())
          {
            continue;
          }

          if (!Network.TryGetPeerNotSynchronized(out Peer peer))
          {
            Blockchain.ReleaseLock();
            continue;
          }

          try
          {
            peer.IsSyncMaster = true;

            await SynchronizeWithPeer(peer);

            peer.IsSyncMaster = false;
            peer.IsSynchronized = true;
          }
          catch (Exception ex)
          {
            string.Format(
              "Exception {0} when syncing with peer {1}: \n{2}",
              ex.GetType(),
              peer.GetID(),
              ex.Message)
              .Log(LogFile);

            peer.FlagDispose = true;

            Network.ReleasePeer(peer);
          }

          Blockchain.ReleaseLock();
        }
      }

      async Task SynchronizeWithPeer(Peer peer)
      {
        string.Format(
          "Synchronize with peer {0}",
          peer.GetID())
          .Log(LogFile);

      LABEL_StageBranch:
        
        List<Header> locator = Blockchain.GetLocator();
        Header headerTip = locator.First();

        Header headerRoot = await peer.GetHeaders(locator);

        if (headerRoot == null)
        {
          return;
        }

        if (headerRoot.HeaderPrevious == headerTip)
        {
          await peer.BuildHeaderchain(
            headerRoot,
            Blockchain.Height + 1);
          
          await TrySynchronizeUTXO(headerRoot, peer);

          return;
        }

        headerRoot = await peer.SkipDuplicates(
          headerRoot,
          locator);

        Blockchain.GetStateAtHeader(
          headerRoot.HeaderPrevious,
          out int heightAncestor,
          out double difficultyAncestor);

        double difficultyFork = difficultyAncestor +
          await peer.BuildHeaderchain(
            headerRoot,
            heightAncestor + 1);

        double difficultyOld = Blockchain.Difficulty;

        if (difficultyFork > difficultyOld)
        {
          if (!await Blockchain.TryLoadImage(
            heightAncestor,
            headerRoot.HashPrevious))
          {
            goto LABEL_StageBranch;
          }

          if (!await TrySynchronizeUTXO(headerRoot, peer))
          {
            Blockchain.Archiver.Dispose();

            await Blockchain.LoadImage();
          }
        }
        else if (difficultyFork < difficultyOld)
        {
          if (peer.IsInbound())
          {
            string.Format("Fork weaker than Main.")
              .Log(LogFile);

            peer.FlagDispose = true;
          }
          else
          {
            peer.SendHeaders(
              new List<Header>() { Blockchain.HeaderTip });
          }
        }
      }

      Header HeaderLoad;
      BufferBlock<Peer> QueueSynchronizer = new BufferBlock<Peer>();
      int IndexBlockArchiveDownload;
      List<UTXOTable.BlockParser> QueueParsersInvalid =
        new List<UTXOTable.BlockParser>();
      List<Peer> PeersDownloading = new List<Peer>();

      int CountMaxDownloadsAwaiting = 3;

      Dictionary<int, 
        KeyValuePair<UTXOTable.BlockParser, Peer>> DownloadsAwaiting = 
        new Dictionary<int,
        KeyValuePair<UTXOTable.BlockParser, Peer>>();

      async Task<bool> TrySynchronizeUTXO(
        Header headerRoot,
        Peer peer)
      {
        var peersCompleted = new List<Peer>();

        var cancellationSynchronizeUTXO = 
          new CancellationTokenSource();

        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        int indexBlockArchiveQueue = 0;

        DownloadsAwaiting.Clear();
        
        TryStartBlockDownload(peer);

        bool flagTimeoutTriggered = false;
        
        do
        {
          while (
            Network.TryGetPeer(out peer) &&
            TryStartBlockDownload(peer)) ;

          if (!PeersDownloading.Any())
          {
            if(
              QueueParsersInvalid.Count == 0 && 
              HeaderLoad == null)
            {
              "UTXO Synchronization completed."
                .Log(LogFile);

              return true;
            }
            else
            {
              if (!flagTimeoutTriggered)
              {
                cancellationSynchronizeUTXO
                  .CancelAfter(TIMEOUT_SYNCHRONIZER);

                flagTimeoutTriggered = true;
              }

              try
              {
                await Task.Delay(
                  2000,
                  cancellationSynchronizeUTXO.Token)
                  .ConfigureAwait(false);

                continue;
              }
              catch (TaskCanceledException)
              {
                "Abort UTXO Synchronization due to timeout."
                  .Log(LogFile);

                QueueParsersInvalid.Clear();
              }
            }

            peersCompleted.ForEach(p => Network.ReleasePeer(p));
            return false;
          }
          else if (flagTimeoutTriggered)
          {
            flagTimeoutTriggered = false;

            cancellationSynchronizeUTXO =
              new CancellationTokenSource();
          }

          peer = await QueueSynchronizer
            .ReceiveAsync()
            .ConfigureAwait(false);
          
          PeersDownloading.Remove(peer);

          var blockParser = peer.BlockParser;
          
          if (peer.FlagDispose)
          {
            Network.ReleasePeer(peer);
          }
          else if (peer.Command == "notfound")
          {
            peersCompleted.Add(peer);
          }
          else
          {
            if (indexBlockArchiveQueue != blockParser.Index)
            {
              var downloadAwaitingInsertion =
                new KeyValuePair<UTXOTable.BlockParser, Peer>(
                  blockParser,
                  peer);

              DownloadsAwaiting.Add(
                blockParser.Index,
                downloadAwaitingInsertion);
              
              if (!blockParser.IsArchiveBufferOverflow)
              {
                peer.BlockParser = null;

                TryStartBlockDownload(peer);
              }

              continue;
            }

            bool isDownloadAwaiting = false;

            if (Blockchain.TryArchiveBlocks(
              blockParser,
              UTXOIMAGE_INTERVAL_SYNC))
            {
            LABEL_PostProcessArchiveBlocks:

              if (blockParser.IsArchiveBufferOverflow)
              {
                blockParser.RecoverFromOverflow();

                RunBlockDownload(
                  peer, 
                  flagContinueDownload: true);

                continue;
              }

              indexBlockArchiveQueue += 1;

              Blockchain.ReleaseBlockParser(blockParser);

              if(!isDownloadAwaiting)
              {
                peer.BlockParser = null;
                TryStartBlockDownload(peer);
              }

              if (!DownloadsAwaiting.TryGetValue(
                indexBlockArchiveQueue,
                out KeyValuePair<UTXOTable.BlockParser, Peer>
                downloadAwaitingInsertion))
              {
                continue;
              }

              isDownloadAwaiting = true;

              DownloadsAwaiting.Remove(
                indexBlockArchiveQueue);

              blockParser = downloadAwaitingInsertion.Key;
              peer = downloadAwaitingInsertion.Value;

              if (Blockchain.TryArchiveBlocks(
                blockParser,
                UTXOIMAGE_INTERVAL_SYNC))
              {
                goto LABEL_PostProcessArchiveBlocks;
              }
            }

            peer.FlagDispose = true;
            Network.ReleasePeer(peer);
          }

          EnqueueParser(blockParser);

        } while (true);
      }

      void EnqueueParser(UTXOTable.BlockParser parser)
      {
        int indexBlockDownload = QueueParsersInvalid
          .FindIndex(a => a.Index > parser.Index);

        if (indexBlockDownload == -1)
        {
          QueueParsersInvalid.Add(parser);
        }
        else
        {
          QueueParsersInvalid.Insert(
            indexBlockDownload,
            parser);
        }
      }
      
      bool TryStartBlockDownload(Peer peer)
      {
        if (QueueParsersInvalid.Any())
        {
          peer.BlockParser = QueueParsersInvalid.First();
          peer.BlockParser.ClearPayloadData();

          QueueParsersInvalid.RemoveAt(0);

          RunBlockDownload(
            peer,
            flagContinueDownload: false);

          return true;
        }

        if (DownloadsAwaiting.Count < CountMaxDownloadsAwaiting)
        {
          var blockParser = Blockchain.GetBlockParser();

          if (HeaderLoad != null)
          {
            blockParser.Index = IndexBlockArchiveDownload;
            IndexBlockArchiveDownload += 1;

            blockParser.HeaderRoot = HeaderLoad;
            blockParser.Height = 0;
            blockParser.Difficulty = 0.0;

            do
            {
              blockParser.HeaderTip = HeaderLoad;
              blockParser.Height += 1;
              blockParser.Difficulty += HeaderLoad.Difficulty;

              HeaderLoad = HeaderLoad.HeaderNext;
            } while (
            blockParser.Height < peer.CountBlocksLoad
            && HeaderLoad != null);

            peer.BlockParser = blockParser;

            RunBlockDownload(
              peer,
              flagContinueDownload: false);

            return true;
          }
        }

        Network.ReleasePeer(peer);
        return false;
      }

      async Task RunBlockDownload(
        Peer peer,
        bool flagContinueDownload)
      {
        PeersDownloading.Add(peer);

        await peer.DownloadBlocks(flagContinueDownload);

        QueueSynchronizer.Post(peer);
      }
    }
  }
}