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
    partial class BlockchainSynchronizer
    {
      Network Network;
      Blockchain Blockchain;


      const int UTXOIMAGE_INTERVAL_SYNC = 500;
      const int UTXOIMAGE_INTERVAL_LISTEN = 50;

      const int TIMEOUT_SYNC_UTXO = 60000;


      StreamWriter LogFile;




      public BlockchainSynchronizer(Blockchain blockchain)
      {
        Blockchain = blockchain;
        Network = new Network(blockchain);

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

        LogFile = new StreamWriter("logSynchronizer", false);
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

          if(!Blockchain.TryLock())
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

          Network.ReleasePeer(peer);

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
          string.Format(
            "Send getheader to peer {0}, \n" +
            "locator: {1} ... {2}",
            peer.GetID(), 
            locator.First().Hash.ToHexString(), 
            locator.Count > 1 ? headerTip.Hash.ToHexString() : "")
            .Log(LogFile);

          Header headerRoot = await peer.GetHeaders(locator);

          if (headerRoot == null)
          {
            return;
          }

          if (headerRoot.HeaderPrevious == headerTip)
          {
            string.Format(
              "Build headerchain from tip {0}",
              headerTip.Hash.ToHexString())
              .Log(LogFile);
            
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
      BufferBlock<Peer> QueueSynchronizer = 
        new BufferBlock<Peer>();
      readonly object LOCK_IndexBlockArchiveQueue = new object();
      int IndexBlockArchiveDownload;
      int IndexBlockArchiveQueue;
      object LOCK_PeersAwaitingInsertion = new object();
      List<Peer> PeersCompleted = new List<Peer>();
      Peer PeerSyncMaster;
      Dictionary<int, Peer> PeersAwaitingInsertion =
        new Dictionary<int, Peer>();
      List<KeyValuePair<int, List<Inventory>>> QueueBlockDownloads =
        new List<KeyValuePair<int, List<Inventory>>>();
      List<Peer> PeersDownloading = new List<Peer>();

      async Task SynchronizeUTXO(
        Header headerRoot,
        Peer peer)
      {
        string.Format(
          "Start SynchronizeUTXO with peer {0}, " +
          "headerRoot {1}",
          peer.GetID(),
          headerRoot.Hash.ToHexString())
          .Log(LogFile);

        int timeout = 30000;
        CancellationTokenSource cancellationSynchronizeUTXO = 
          new CancellationTokenSource();
        
        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        IndexBlockArchiveQueue = 0;

        PeerSyncMaster = peer;
        
      LOOP_QueueSynchronization:

        bool flagTimeoutTriggered = false;

        do
        {
          StartBlockDownloads(peer);

          if (!PeersDownloading.Any())
          {
            if(!flagTimeoutTriggered)
            {
              cancellationSynchronizeUTXO.CancelAfter(timeout);
              flagTimeoutTriggered = true;
            }

            await Task.Delay(
              3000, 
              cancellationSynchronizeUTXO.Token)
              .ConfigureAwait(false);

            continue;
          }

          while (true)
          {
            try
            {
              peer = await QueueSynchronizer
                .ReceiveAsync(cancellationSynchronizeUTXO.Token)
                .ConfigureAwait(false);

              break;
            }
            catch (TaskCanceledException)
            {
              "Timeout occured when awaiting QueueSynchronizer."
                .Log(LogFile);

              if (PeersDownloading.Any())
              {
                cancellationSynchronizeUTXO =
                  new CancellationTokenSource(timeout);
              }
              else
              {
                "Abort UTXOSynchronization".Log(LogFile);

                QueueBlockDownloads.Clear();

                // Release all completed and awaiting peers

                Blockchain.Archiver.Dispose();

                await Blockchain.LoadImage();

                return;
              }
            }
          }

          PeersDownloading.Remove(peer);

          if (peer.BlockArchive.IsInvalid)
          {
            if (peer == PeerSyncMaster)
            {
              peer.FlagDispose = true;
            }
          }
          else
          {
            if (IndexBlockArchiveQueue != peer.BlockArchive.Index)
            {
              PeersAwaitingInsertion.Add(
                IndexBlockArchiveQueue,
                peer);

              continue;
            }

            while(true)
            {
              if (Blockchain.TryArchiveBlockArchive(
                peer.BlockArchive,
                UTXOIMAGE_INTERVAL_SYNC))
              {
                if (HeaderLoad != null)
                {
                  StartBlockDownload(peer);
                }

                IndexBlockArchiveQueue += 1;

                if (!PeersAwaitingInsertion.TryGetValue(
                  IndexBlockArchiveQueue, out peer))
                {
                  goto LOOP_QueueSynchronization;
                }

                PeersAwaitingInsertion.Remove(IndexBlockArchiveQueue);
              }

              peer.FlagDispose = true;
              break;
            }
          }

          try
          {
            KeyValuePair<int, Peer> peerkeyValue =
              PeersAwaitingInsertion.First(p =>
              !p.Value.FlagDispose &&
              p.Value.BlockArchive.Index > peer.BlockArchive.Index);

            PeersAwaitingInsertion.Remove(peerkeyValue.Key);

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
          
        } while (true);
      }

      void StartBlockDownloads(Peer peer)
      {
        while (
          (QueueBlockDownloads.Any() || HeaderLoad != null) &&
          (peer != null || Network.TryGetPeer(out peer)))
        {
          StartBlockDownload(peer);

          peer = null;
        }
      }

      async Task StartBlockDownload(Peer peer)
      {
        if (QueueBlockDownloads.Any())
        {
          KeyValuePair<int, List<Inventory>> blockDownloadWaiting =
            QueueBlockDownloads.First();

          QueueBlockDownloads.RemoveAt(0);

          peer.BlockArchive.Index = blockDownloadWaiting.Key;
          peer.Inventories = blockDownloadWaiting.Value;
        }
        else
        {
          peer.BlockArchive.Index = IndexBlockArchiveDownload;
          IndexBlockArchiveDownload += 1;

          peer.CreateInventories(ref HeaderLoad);
        }

        await RunBlockDownload(peer);
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