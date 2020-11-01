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
      
      const int UTXOIMAGE_INTERVAL_SYNC = 500;
      const int UTXOIMAGE_INTERVAL_LISTEN = 50;

      const int TIMEOUT_SYNC_UTXO = 60000;


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

        try
        {
          Directory.Delete("J:\\BlockArchivePartitioned", true);
        }
        catch (Exception ex)
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
          
          await SynchronizeWithPeer(peer);

          peer.IsSynchronized = true;

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
        Header headerTip = locator.Last();

        try
        {
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

            await SynchronizeUTXO(headerRoot, peer);
            
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

            await SynchronizeUTXO(headerRoot, peer);
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
        catch (Exception ex)
        {
          string.Format(
            "Exception {0} when syncing with peer {1}: \n{2}",
            ex.GetType(),
            peer.GetID(),
            ex.Message)
            .Log(LogFile);

          peer.FlagDispose = true;
        }
      }

      Header HeaderLoad;
      BufferBlock<Peer> QueueSynchronizer = new BufferBlock<Peer>();
      int IndexBlockArchiveDownload;
      List<KeyValuePair<int, List<Inventory>>> QueueBlockDownloads =
        new List<KeyValuePair<int, List<Inventory>>>();
      List<Peer> PeersDownloading = new List<Peer>();

      async Task SynchronizeUTXO(
        Header headerRoot,
        Peer peer)
      {
        Peer peerSyncMaster = peer;
        var peersAwaitingInsertion = new Dictionary<int, Peer>();
        var peersCompleted = new List<Peer>();

        int timeout = 30000;
        var cancellationSynchronizeUTXO = 
          new CancellationTokenSource();

        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        int indexBlockArchiveQueue = 0;
        
        TryStartBlockDownload(peer);

      LOOP_QueueSynchronization:

        bool flagTimeoutTriggered = false;

        do
        {
          while (
            Network.TryGetPeer(out peer) &&
            TryStartBlockDownload(peer)) ;

          if (!PeersDownloading.Any())
          {
            if(!QueueBlockDownloads.Any() && HeaderLoad == null)
            {
              "UTXO Synchronization completed.".Log(LogFile);
            }
            else
            {
              if (!flagTimeoutTriggered)
              {
                cancellationSynchronizeUTXO.CancelAfter(timeout);
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

                QueueBlockDownloads.Clear();

                peersAwaitingInsertion.ToList().ForEach(
                  p =>
                  {
                    p.Value.BlockArchivesDownloaded.Clear();
                    Network.ReleasePeer(p.Value);
                  });

                Blockchain.Archiver.Dispose();

                await Blockchain.LoadImage();
              }
            }

            peersCompleted.ForEach(p => Network.ReleasePeer(p));
            return;
          }
          else if (flagTimeoutTriggered)
          {
            flagTimeoutTriggered = false;

            cancellationSynchronizeUTXO =
              new CancellationTokenSource();
          }

          peer = await QueueSynchronizer.ReceiveAsync()
            .ConfigureAwait(false);

          PeersDownloading.Remove(peer);

          if (peer.BlockArchive.IsInvalid)
          {
            if (peer == peerSyncMaster)
            {
              peer.FlagDispose = true;
            }
            else
            {
              peersCompleted.Add(peer);
            }
          }
          else
          {
            if (indexBlockArchiveQueue != peer.BlockArchive.Index)
            {
              peersAwaitingInsertion.Add(
                peer.BlockArchive.Index,
                peer);

              continue;
            }

            while (Blockchain.TryArchiveBlockArchive(
                peer.BlockArchive,
                UTXOIMAGE_INTERVAL_SYNC))
            {
              if (peer.TryPopBlockArchive())
              {
                peersAwaitingInsertion.Add(
                  peer.BlockArchive.Index,
                  peer);
              }
              else
              {
                TryStartBlockDownload(peer);
              }

              indexBlockArchiveQueue += 1;

              if (!peersAwaitingInsertion.TryGetValue(
                indexBlockArchiveQueue, out peer))
              {
                goto LOOP_QueueSynchronization;
              }

              peersAwaitingInsertion.Remove(
                indexBlockArchiveQueue);
            }

            peer.FlagDispose = true;
          }

          try
          {
            KeyValuePair<int, Peer> peerkeyValue =
              peersAwaitingInsertion.First(p =>
              !p.Value.FlagDispose &&
              !peersCompleted.Contains(p.Value) &&
              p.Value.BlockArchive.Index > peer.BlockArchive.Index);

            peersAwaitingInsertion.Remove(peerkeyValue.Key);

            Peer peerNew = peerkeyValue.Value;

            peerNew.PushBlockArchive(peer);

            RunBlockDownload(peerNew);
          }
          catch (InvalidOperationException)
          {
            var blockDownload = new KeyValuePair<int, List<Inventory>>(
              peer.BlockArchive.Index,
              peer.Inventories);

            int indexBlockDownload = QueueBlockDownloads
              .FindIndex(b => b.Key > blockDownload.Key);

            if (indexBlockDownload == -1)
            {
              QueueBlockDownloads.Add(blockDownload);
            }
            else
            {
              QueueBlockDownloads.Insert(
                indexBlockDownload,
                blockDownload);
            }
          }

          if (peer.TryPopBlockArchive())
          {
            peersAwaitingInsertion.Add(
              peer.BlockArchive.Index, 
              peer);
          }

        } while (true);
      }
      
      bool TryStartBlockDownload(Peer peer)
      {
        if (QueueBlockDownloads.Any())
        {
          KeyValuePair<int, List<Inventory>> blockDownloadWaiting =
            QueueBlockDownloads.First();

          QueueBlockDownloads.RemoveAt(0);

          peer.BlockArchive.Index = blockDownloadWaiting.Key;
          peer.Inventories = blockDownloadWaiting.Value;

          RunBlockDownload(peer);
          return true;
        }

        if (HeaderLoad != null)
        {
          peer.BlockArchive.Index = IndexBlockArchiveDownload;
          IndexBlockArchiveDownload += 1;

          peer.CreateInventories(ref HeaderLoad);

          RunBlockDownload(peer);
          return true;
        }

        Network.ReleasePeer(peer);
        return false;
      }

      async Task RunBlockDownload(Peer peer)
      {
        PeersDownloading.Add(peer);

        await peer.DownloadBlocks();

        QueueSynchronizer.Post(peer);
      }
      
    }
  }
}