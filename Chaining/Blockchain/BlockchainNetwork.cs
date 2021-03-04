using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net;
using System.Net.Sockets;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainNetwork
    {
      Blockchain Blockchain;

      int TIMEOUT_SYNCHRONIZER = 30000;

      const int UTXOIMAGE_INTERVAL_SYNC = 100;
      const int UTXOIMAGE_INTERVAL_LISTEN = 5;

      StreamWriter LogFile;

      const UInt16 Port = 8333;

      const int COUNT_PEERS_MAX = 4;

      object LOCK_Peers = new object();
      List<Peer> Peers = new List<Peer>();

      static DirectoryInfo DirectoryLogPeers;
      static DirectoryInfo DirectoryLogPeersDisposed;



      public BlockchainNetwork(Blockchain blockchain)
      {
        Blockchain = blockchain;

        LogFile = new StreamWriter("logSynchronizer", false);

        DirectoryLogPeers = Directory.CreateDirectory(
          "logPeers");

        DirectoryLogPeersDisposed = Directory.CreateDirectory(
          Path.Combine(
            DirectoryLogPeers.FullName,
            "disposed"));
      }



      public void Start()
      {
        "Start Network.".Log(LogFile);

        StartConnector();

        StartSynchronizer();

        //"Start listener for inbound connection requests."
        //  .Log(LogFile);
      }

      async Task StartConnector()
      {
        int countPeersToCreate;

        while (true)
        {
          lock (LOCK_Peers)
          {
            List<Peer> peersDispose =
              Peers.FindAll(p => p.FlagDispose && !p.IsBusy);

            peersDispose.ForEach(p =>
            {
              Peers.Remove(p);
              p.Dispose();
            });

            countPeersToCreate = COUNT_PEERS_MAX - Peers.Count;
          }

          if (countPeersToCreate > 0)
          {
            string.Format(
              "Connect with {0} new peers. " +
              "{1} peers connected currently.",
              countPeersToCreate,
              Peers.Count)
              .Log(LogFile);

            List<IPAddress> iPAddresses =
              RetrieveIPAddresses(countPeersToCreate);

            if (iPAddresses.Count > 0)
            {
              var createPeerTasks = new Task[iPAddresses.Count()];

              Parallel.For(
                0,
                countPeersToCreate,
                i => createPeerTasks[i] = CreatePeer(iPAddresses[i]));

              await Task.WhenAll(createPeerTasks);
            }
          }

          await Task.Delay(10000).ConfigureAwait(false);
        }
      }

      List<IPAddress> RetrieveIPAddresses(int countMax)
      {
        List<IPAddress> iPAddresses = new List<IPAddress>();

        while (iPAddresses.Count < countMax)
        {
          if (AddressPool.Count == 0)
          {
            DownloadIPAddressesFromSeeds();

            lock (LOCK_Peers)
            {
              AddressPool.RemoveAll(
                a => Peers.Any(p => p.GetID() == a.ToString()));
            }

            foreach (FileInfo file in
              DirectoryLogPeersDisposed.GetFiles())
            {
              if (DateTime.Now.Subtract(
                file.LastAccessTime).TotalHours > 24)
              {
                file.Delete();
              }
              else
              {
                AddressPool.RemoveAll(
                  a => a.ToString() == file.Name);
              }
            }
                        
            if (AddressPool.Count == 0)
            {
              return iPAddresses;
            }
          }

          int randomIndex = RandomGenerator
            .Next(AddressPool.Count);

          IPAddress iPAddress = AddressPool[randomIndex];
          AddressPool.Remove(iPAddress);
          
          if (iPAddresses.Any(
            ip => ip.ToString() == iPAddress.ToString()))
          {
            continue;
          }

          iPAddresses.Add(iPAddress);
        }

        return iPAddresses;
      }

      async Task CreatePeer(IPAddress iPAddress)
      {
        var peer = new Peer(Blockchain, iPAddress);

        try
        {
          await peer.Connect(Port);
        }
        catch (Exception ex)
        {
          string.Format(
            "Exception {0} when connecting with peer {1}: \n{2}",
            ex.GetType(),
            peer.GetID(),
            ex.Message)
            .Log(LogFile);

          peer.FlagDispose = true;
        }

        lock (LOCK_Peers)
        {
          Peers.Add(peer);
        }
      }


      List<IPAddress> AddressPool = new List<IPAddress>();
      Random RandomGenerator = new Random();

      void DownloadIPAddressesFromSeeds()
      {
        string pathFileSeeds = @"..\..\DNSSeeds";
        string[] dnsSeeds;

        while (true)
        {
          try
          {
            dnsSeeds = File.ReadAllLines(pathFileSeeds);

            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "{0} when reading file with DNS seeds {1} \n" +
              "{2} \n" +
              "Try again in 10 seconds ...",
              ex.GetType().Name,
              pathFileSeeds,
              ex.Message);

            Thread.Sleep(10000);
          }
        }

        foreach (string dnsSeed in dnsSeeds)
        {
          if (dnsSeed.Substring(0, 2) == "//")
          {
            continue;
          }

          IPAddress[] dnsSeedAddresses;

          try
          {
            dnsSeedAddresses = 
              Dns.GetHostEntry(dnsSeed).AddressList;
          }
          catch(Exception ex)
          {
            // If error persists, remove seed from file.

            string.Format(
              "Cannot get peer address from dns server {0}: \n{1}",
              dnsSeed,
              ex.Message)
              .Log(LogFile);

            continue;
          }

          AddressPool = AddressPool.Union(
            dnsSeedAddresses.Distinct())
            .ToList();
        }
      }
    


      async Task StartSynchronizer()
      {
        "Start network synchronization.".Log(LogFile);
        
        while (true)
        {
          await Task.Delay(3000).ConfigureAwait(false);

          if (!Blockchain.TryLock())
          {
            continue;
          }

          if (!TryGetPeerNotSynchronized(
            out Peer peer))
          {
            Blockchain.ReleaseLock();
            continue;
          }

          try
          {
            string.Format(
              "Synchronize with peer {0}",
              peer.GetID())
              .Log(LogFile);

            await SynchronizeWithPeer(peer);

            "UTXO Synchronization completed."
              .Log(LogFile);
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

          ReleasePeer(peer);

          Blockchain.ReleaseLock();
        }
      }

      bool TryGetPeerNotSynchronized(
        out Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(p =>
           !p.IsSynchronized &&
           !p.FlagDispose &&
           !p.IsBusy);

          if (peer != null)
          {
            peer.IsBusy = true;
            peer.IsSynchronized = true;
            return true;
          }

          return false;
        }
      }

      void ReleasePeer(Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer.IsBusy = false;
        }
      }


      async Task SynchronizeWithPeer(Peer peer)
      {
      LABEL_SynchronizeWithPeer:
        
        peer.HeaderDownload = new HeaderDownload();
        peer.HeaderDownload.Locator = Blockchain.GetLocator();

        Header headerRoot = await peer.GetHeaders();

        if (headerRoot == null)
        {
          return;
        }

        if (headerRoot.HeaderPrevious == Blockchain.HeaderTip)
        {
          await peer.BuildHeaderchain(
            headerRoot,
            Blockchain.Height + 1);
          
          await TrySynchronizeUTXO(headerRoot, peer);

          return;
        }

        headerRoot = await peer.SkipDuplicates(headerRoot);

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
          await Blockchain.LoadImage(
             heightAncestor,
             headerRoot.HashPrevious);

          if (Blockchain.Height != heightAncestor)
          {
            goto LABEL_SynchronizeWithPeer;
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
      int IndexBlockDownload;
      List<BlockDownload> QueueDownloadsInvalid =
        new List<BlockDownload>();
      List<Peer> PeersDownloading = new List<Peer>();

      int CountMaxDownloadsAwaiting = 10;

      Dictionary<int, BlockDownload> DownloadsAwaiting =
        new Dictionary<int, BlockDownload>();

      async Task<bool> TrySynchronizeUTXO(
        Header headerRoot,
        Peer peer)
      {
        var peersCompleted = new List<Peer>();

        var cancellationSynchronizeUTXO =
          new CancellationTokenSource();

        HeaderLoad = headerRoot;
        IndexBlockDownload = 0;
        int indexBlockDownloadQueue = 0;

        DownloadsAwaiting.Clear();

        Peer peerSyncMaster = peer;

        StartBlockDownload(peer);

        bool flagTimeoutTriggered = false;

        do
        {
          if (IsBlockDownloadAvailable())
          {
            if (TryGetPeer(out peer))
            {
              StartBlockDownload(peer);
              continue;
            }

            if (PeersDownloading.Count == 0)
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

                QueueDownloadsInvalid.Clear();

                peersCompleted.ForEach(p => ReleasePeer(p));
                return false;
              }
            }

            if (flagTimeoutTriggered)
            {
              flagTimeoutTriggered = false;

              cancellationSynchronizeUTXO =
                new CancellationTokenSource();
            }
          }
          else 
          if (PeersDownloading.Count == 0)
          {
            return true;
          }

          peer = await QueueSynchronizer
            .ReceiveAsync()
            .ConfigureAwait(false);

          PeersDownloading.Remove(peer);

          var blockDownload = peer.BlockDownload;

          if (peer == peerSyncMaster &&
            (peer.FlagDispose || peer.Command == Peer.COMMAND_NOTFOUND))
          {
            peer.FlagDispose = true;
          }
          else if (peer.FlagDispose)
          {
            Console.WriteLine(
              "Release peer {0} on line 524",
              peer.GetID());

            ReleasePeer(peer);
          }
          else if (peer.Command == Peer.COMMAND_NOTFOUND)
          {
            peersCompleted.Add(peer);
          }
          else
          {
            if (indexBlockDownloadQueue != blockDownload.Index)
            {
              DownloadsAwaiting.Add(
                blockDownload.Index,
                blockDownload);

              blockDownload.Peer = peer;

              if (IsBlockDownloadAvailable())
              {
                StartBlockDownload(peer);
              }
              else
              {
                Console.WriteLine(
                  "Release peer {0} on line 318",
                  peer.GetID());

                ReleasePeer(peer);
              }

              continue;
            }

            bool isDownloadAwaiting = false;

            if (TryInsertBlocks(blockDownload))
            {
            LABEL_PostProcessArchiveBlocks:
              
              indexBlockDownloadQueue += 1;

              if (!isDownloadAwaiting)
              {
                if(IsBlockDownloadAvailable())
                {
                  StartBlockDownload(peer);
                }
                else
                {
                  ReleasePeer(peer);
                }
              }

              if (!DownloadsAwaiting.TryGetValue(
                indexBlockDownloadQueue,
                out blockDownload))
              {
                continue;
              }

              isDownloadAwaiting = true;

              DownloadsAwaiting.Remove(
                indexBlockDownloadQueue);

              peer = blockDownload.Peer;

              if (TryInsertBlocks(blockDownload))
              {
                goto LABEL_PostProcessArchiveBlocks;
              }
            }
            
            peer.FlagDispose = true;

            if (peer != peerSyncMaster)
            {
              Console.WriteLine(
                "Release peer {0} on line 387",
                peer.GetID());

              ReleasePeer(peer);
            }
          }

          EnqueueBlockDownloadInvalid(blockDownload);

        } while (true);
      }

      void StartBlockDownload(Peer peer)
      {
        if (QueueDownloadsInvalid.Any())
        {
          peer.BlockDownload = QueueDownloadsInvalid.First();
          QueueDownloadsInvalid.RemoveAt(0);
        }
        else
        {
          peer.BlockDownload = new BlockDownload(
            IndexBlockDownload,
            ref HeaderLoad,
            peer.CountBlocksLoad);

          IndexBlockDownload += 1;
        }

        RunBlockDownload(
          peer,
          flagContinueDownload: false);
      }

      async Task RunBlockDownload(
        Peer peer,
        bool flagContinueDownload)
      {
        PeersDownloading.Add(peer);

        await peer.DownloadBlocks(flagContinueDownload);

        QueueSynchronizer.Post(peer);
      }

      bool TryGetPeer(
        out Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(
            p => !p.FlagDispose && !p.IsBusy);

          if (peer != null)
          {
            Console.WriteLine("Get peer {0}", peer.GetID());
            peer.IsBusy = true;
            return true;
          }
        }

        return false;
      }

      bool IsBlockDownloadAvailable()
      {
        return
          DownloadsAwaiting.Count < CountMaxDownloadsAwaiting &&
          (QueueDownloadsInvalid.Count > 0 || HeaderLoad != null);
      }

      void EnqueueBlockDownloadInvalid(
        BlockDownload download)
      {
        int indexdownload = QueueDownloadsInvalid
          .FindIndex(b => b.Index > download.Index);

        if (indexdownload == -1)
        {
          QueueDownloadsInvalid.Add(download);
        }
        else
        {
          QueueDownloadsInvalid.Insert(
            indexdownload,
            download);
        }

        download.IndexHeaderExpected = 0;
        download.Blocks.Clear();
      }



      bool TryInsertBlocks(BlockDownload blockDownload)
      {
        foreach (Block block in blockDownload.Blocks)
        {
          try
          {
            Console.WriteLine(
              "Insert block {0} from download {1}", 
              block.Header.Hash.ToHexString(),
              blockDownload.Index);

            Blockchain.InsertBlock(
                block,
                flagValidateHeader: false);
          }
          catch (Exception ex)
          {
            // TODO: Reorg download, restore utxo

            Console.WriteLine(
              "{0} when inserting block {1}:\n {2}" +
              "TODO: Reorg download, restore utxo",
              ex.GetType().Name,
              block.Header.Hash.ToHexString(),
              ex.Message);

            return false;
          }

          Blockchain.Archiver.ArchiveBlock(
              block,
              UTXOIMAGE_INTERVAL_SYNC);
        }

        return true;
      }
          


      const int PEERS_COUNT_INBOUND = 8;
      TcpListener TcpListener =
        new TcpListener(IPAddress.Any, Port);

      async Task StartPeerInboundListener()
      {
        TcpListener.Start(PEERS_COUNT_INBOUND);

        while (true)
        {
          TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync().
            ConfigureAwait(false);

          string.Format("Received inbound request from {0}",
            tcpClient.Client.RemoteEndPoint.ToString())
            .Log(LogFile);

          var peer = new Peer(tcpClient, Blockchain);

          try
          {
            peer.StartMessageListener();
          }
          catch (Exception ex)
          {
            string.Format(
              "Failed to start listening to inbound peer {0}: " +
              "\n{1}: {2}",
              peer.GetID(),
              ex.GetType().Name,
              ex.Message)
              .Log(LogFile);

            continue;
          }

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }
        }
      }
    }
  }
}