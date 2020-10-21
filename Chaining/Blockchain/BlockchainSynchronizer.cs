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

          if (!Network.TryGetPeerNotSynchronized(
            out Peer peer))
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

              peer.IsDisposed = true;
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

          peer.IsDisposed = true;
        }
      }

      Header HeaderLoad;
      BufferBlock<Peer> QueueSynchronizer = 
        new BufferBlock<Peer>();
      readonly object LOCK_IndexBlockArchiveQueue = new object();
      int IndexBlockArchiveDownload;
      int IndexBlockArchiveQueue;
      object LOCK_PeersSessionRunning = new object();
      List<Peer> PeersSessionRunning = new List<Peer>();
      object LOCK_PeersAwaitingInsertion = new object();
      List<Peer> PeersCompleted = new List<Peer>();
      Peer PeerSyncMaster;
      Dictionary<int, Peer> PeersAwaitingInsertion =
        new Dictionary<int, Peer>();

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
        
        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        IndexBlockArchiveQueue = 0;

        PeerSyncMaster = peer;

        PeersSessionRunning.Add(peer);
        RunBlockDownload(peer);
                
        do
        {
          while (HeaderLoad != null &&
            Network.TryGetPeer(out peer))
          {
            PeersSessionRunning.Add(peer);
            RunBlockDownload(peer);
          }

          peer = await QueueSynchronizer.ReceiveAsync()
            .ConfigureAwait(false);

          if(IndexBlockArchiveQueue != peer.BlockArchive.Index)
          {
            PeersAwaitingInsertion.Add(
              IndexBlockArchiveQueue,
              peer);

            continue;
          }

          if (peer.BlockArchive.IsInvalid)
          {
            if (peer == PeerSyncMaster)
            {
              peer.IsDisposed = true;
            }
          }
          else
          {
            if (Blockchain.TryArchiveBlockArchive(
              peer.BlockArchive,
              UTXOIMAGE_INTERVAL_SYNC))
            {
              if (peer.BlockArchive.IsLastArchive)
              {
                return;
              }

              if (HeaderLoad != null)
              {
                RunBlockDownload(peer);
              }

              IndexBlockArchiveQueue += 1;

              if (PeersAwaitingInsertion.TryGetValue(
                IndexBlockArchiveQueue,
                out peer))
              {
                PeersAwaitingInsertion.Remove(IndexBlockArchiveQueue);
                continue;
              }
              else
              {
                break;
              }
            }

            peer.IsDisposed = true;
          }


          // freier Peer suchen

          // Zuerst in PeersAwaiting suchen, dann Queueu awaiten. 
          // Nach Timeout Synchronization abbrechen.



          if (
            peer.BlockArchive.IsInvalid ||
            !Blockchain.TryArchiveBlockArchive(
              peer.BlockArchive,
              UTXOIMAGE_INTERVAL_SYNC))
          {
            CancellationToken cancellationToken =
              new CancellationTokenSource(TIMEOUT_SYNC_UTXO).Token;
            int countWaitingCycles = 0;

            Peer peerNew;

            while (true)
            {
              lock (LOCK_PeersAwaitingInsertion)
              {
                if (PeersAwaitingInsertion.Any())
                {
                  peerNew = PeersAwaitingInsertion[0];
                  PeersAwaitingInsertion.Remove(peerNew);
                  break;
                }
              }

              if (Network.TryGetPeer(out peerNew))
              {
                break;
              }

              try
              {
                await Task.Delay(1000, cancellationToken)
                  .ConfigureAwait(false);
              }
              catch (TaskCanceledException)
              {
                string.Format(
                  "Invalid blockArchive {0}. " +
                  "Timeout while waiting for new peer for download.",
                  peer.BlockArchive.Index)
                  .Log(LogFile);
              }
            }

            string.Format(
              "Invalid blockArchive {0}, " +
              "waiting for new peer for download.\n" +
              "Timeout in {1} seconds.",
              peer.BlockArchive.Index,
              TIMEOUT_SYNC_UTXO / 1000 - ++countWaitingCycles)
              .Log(LogFile);

            peerNew.Inventories = peer.Inventories;
            peerNew.BlockArchive = peer.BlockArchive;

            peerNew.DownloadBlocks();

            if (peer == PeerSyncMaster)
            {
              peer.IsDisposed = true;
            }

            PeersSessionRunning.Remove(peer);
            PeersCompleted.Add(peer);
          }
          else
          {
            // Wenn kein Peer den korrekten
            // Block liefert, soll abgebrochen werden.
            string.Format(
              "Failed to insert blockArchive {0} " +
              "when syncing with peer {1}.",
              peer.BlockArchive.Index,
              peer.GetID())
              .Log(LogFile);

            peer.IsDisposed = true;

            PeersSessionRunning.Remove(peer);
            PeersCompleted.Add(peer);

            Blockchain.Archiver.Dispose();

            await Blockchain.LoadImage();

            string.Format(
              "Blockchain height {0} after Loading.",
              Blockchain.Height)
              .Log(LogFile);
          }


        } while (true);
      }
           
      async Task RunBlockDownload(Peer peer)
      {
        peer.BlockArchive.Index = IndexBlockArchiveDownload;
        IndexBlockArchiveDownload += 1;

        peer.CreateInventories(ref HeaderLoad);

        await peer.DownloadBlocks();

        QueueSynchronizer.Post(peer);
      }
      
    }
  }
}