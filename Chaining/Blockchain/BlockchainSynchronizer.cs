using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net;
using System.Net.Sockets;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainSynchronizer
    {
      Blockchain Blockchain;

      const UInt16 Port = 8333;

      const int UTXOIMAGE_INTERVAL_SYNC = 500;
      const int UTXOIMAGE_INTERVAL_LISTEN = 50;
      const int COUNT_PEERS_MAX = 1;

      object LOCK_Peers = new object();
      List<Peer> Peers = new List<Peer>();
      object LOCK_PeersLoadBalancing = new object();
      List<Peer> PeersLoadBalancing = new List<Peer>();

      readonly object LOCK_IsBlockchainLocked = new object();
      bool IsBlockchainLocked;
      
      StreamWriter LogFile;




      public BlockchainSynchronizer(Blockchain blockchain)
      {
        Blockchain = blockchain;

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

        StartPeerGenerator();

        // StartPeerInboundListener();
      }

                                         
      
      async Task StartPeerSynchronizer()
      {
        "Start Peer synchronizer".Log(LogFile);

        Peer peer;

        while (true)
        {
          await Task.Delay(2000).ConfigureAwait(false);

          peer = null;

          lock (LOCK_Peers)
          {
            peer = Peers.Find(p => !p.IsSynchronized);

            if (peer == null)
            {
              continue;
            }

            lock (LOCK_IsBlockchainLocked)
            {
              if (IsBlockchainLocked)
              {
                continue;
              }

              IsBlockchainLocked = true;
            }

            peer.SetStatusBusy();

            Peers.Remove(peer);
          }

          await SynchronizeWithPeer(peer);

          peer.IsSynchronized = true;

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }

          IsBlockchainLocked = false;
        }
      }

      async Task SynchronizeWithPeer(Peer peer)
      {
        string.Format(
          "Synchronize with peer {0}",
          peer.GetIdentification())
          .Log(LogFile);

      LABEL_StageBranch:

        List<Header> locator = Blockchain.GetLocator();
        Header headerTip = locator.Last();

        try
        {
          string.Format(
            "Send getheader to peer {0}, \n" +
            "locator: {1} ... {2}",
            peer.GetIdentification(), 
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
            peer.GetIdentification(),
            ex.Message)
            .Log(LogFile);

          peer.IsDisposed = true;
        }
      }

      Header HeaderLoad;
      BufferBlock<Peer> QueueSynchronizer = new BufferBlock<Peer>();
      readonly object LOCK_IndexBlockArchiveQueue = new object();
      int IndexBlockArchiveDownload;
      int IndexBlockArchiveQueue;

      async Task SynchronizeUTXO(
        Header headerRoot,
        Peer peer)
      {
        string.Format(
          "Start SynchronizeUTXO, headerRoot {0}",
          headerRoot.Hash.ToHexString())
          .Log(LogFile);

        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        IndexBlockArchiveQueue = 0;

        try
        {
          Task taskUTXOSyncSessions = StartUTXOSyncSessions(peer);

          while (true)
          {
            peer = await QueueSynchronizer.ReceiveAsync()
              .ConfigureAwait(false);
            // Hier müssen auch die abgekackten peers übergeben werden.
            // Dann kann der Inserter wissen ob er selbst auch abkacken 
            // soll oder nicht.

            UTXOTable.BlockArchive blockArchive =
              peer.BlockArchivesDownloaded.Pop();

            blockArchive.Index = Blockchain.Archiver.IndexBlockArchive;

            Blockchain.InsertBlockArchive(blockArchive);

            Blockchain.Archiver.ArchiveBlock(
              blockArchive, UTXOIMAGE_INTERVAL_SYNC);

            if (blockArchive.IsLastArchive)
            {
              peer.SetUTXOSyncComplete();
              break;
            }

            RunUTXOSyncSession(peer);
          }

          await taskUTXOSyncSessions;
        }
        catch (Exception ex)
        {
          string.Format(
            "Exception {0} when syncing with peer {1}: \n{2}",
            ex.GetType(),
            peer.GetIdentification(),
            ex.Message)
            .Log(LogFile);

          peer.SetUTXOSyncComplete();
          peer.IsDisposed = true;

          Blockchain.Archiver.Dispose();

          await Blockchain.LoadImage();

          string.Format(
            "Blockchain height {0} after Loading.",
            Blockchain.Height)
            .Log(LogFile);
        }
      }
      

      async Task StartUTXOSyncSessions(Peer peer)
      {
        RunUTXOSyncSession(peer);

        List<Peer> peersLoadBalancingNew;

        while (true)
        {
          lock (LOCK_PeersLoadBalancing)
          {
            if (
              peer.IsUTXOSyncComplete() &&
              PeersLoadBalancing.All(p => p.IsUTXOSyncComplete()))
            {
              lock(LOCK_Peers)
              {
                Peers.Add(peer);
                Peers.AddRange(PeersLoadBalancing);
              }

              PeersLoadBalancing.Clear();

              return;
            }
            
            lock (LOCK_Peers)
            {
              peersLoadBalancingNew = 
                Peers.Where(p => !p.IsDisposed)
                .ToList();

              Peers = Peers.Except(peersLoadBalancingNew).ToList();
            }
          }

          if (peersLoadBalancingNew.Count > 0)
          {
            peersLoadBalancingNew.ForEach(p => p.SetStatusBusy());
            peersLoadBalancingNew.Select(p => RunUTXOSyncSession(p))
              .ToList();
          }

          await Task.Delay(1000).ConfigureAwait(false);
        }
      }


      readonly object LOCK_HeaderLoad = new object();

      async Task RunUTXOSyncSession(Peer peer)
      {
        string.Format(
         "Run UTXO Sync session with peer {0}",
         peer.GetIdentification())
         .Log(LogFile);

        if (peer.BlockArchivesDownloaded.Count == 0)
        {
          lock (LOCK_HeaderLoad)
          {
            if (HeaderLoad == null)
            {
              lock (LOCK_PeersLoadBalancing)
              {
                peer.SetUTXOSyncComplete();
              }

              return;
            }

            peer.BlockArchive.Index = IndexBlockArchiveDownload;
            IndexBlockArchiveDownload += 1;

            peer.CreateInventories(ref HeaderLoad);
          }

          while (!await peer.TryDownloadBlocks())
          {
            var blockArchive = peer.BlockArchive;
            
            string.Format(
              "Failed to download blockArchive {0} with peer {1}",
              blockArchive.Index,
              peer.GetIdentification())
              .Log(LogFile);

            if (peer.Command == "notfound")
            {
              string.Format(
                "{0}: Did not not find block in blockArchive {1}",
                peer.GetIdentification(),
                blockArchive.Index)
                .Log(LogFile);

              lock (LOCK_PeersLoadBalancing)
              {
                if (!PeersLoadBalancing.Contains(peer))
                {
                  peer.IsDisposed = true;
                }
              }
            }
            else
            {
              peer.IsDisposed = true;
            }

            peer.SetUTXOSyncComplete();

            while (true)
            {
              if (PeersLoadBalancing.All(p => p.IsUTXOSyncComplete()))
              {
                // Allenfalls hier peer übergeben
                return;
              }

              lock (LOCK_PeersLoadBalancing)
              {
                peer = PeersLoadBalancing.Find(
                  p => p.IsStatusAwaitingInsertion() &&
                  p.BlockArchivesDownloaded.Peek().Index >
                  blockArchive.Index);

                if (peer != null)
                {
                  peer.BlockArchive = blockArchive;
                  peer.SetStatusBusy();
                  break;
                }
              }

              await Task.Delay(1000)
                .ConfigureAwait(false);
            }
          }

          string.Format(
            "Downloaded blockArchive {0} with {1} blocks from peer {2}",
            peer.BlockArchive.Index,
            peer.BlockArchive.Height,
            peer.GetIdentification())
            .Log(LogFile);
        }

        lock (LOCK_IndexBlockArchiveQueue)
        {
          if (peer.BlockArchivesDownloaded.Peek().Index !=
            IndexBlockArchiveQueue)
          {
            peer.SetStatusAwaitingInsertion();
            return;
          }
        }

        while (true)
        {
          string.Format("queueu peer {0}, with blockArchive {1}",
            peer.GetIdentification(),
            peer.BlockArchive.Index)
            .Log(LogFile);

          QueueSynchronizer.Post(peer);

          lock (LOCK_IndexBlockArchiveQueue)
          {
            IndexBlockArchiveQueue += 1;
          }

          lock (LOCK_Peers)
          {
            peer = Peers.Find(p =>
            p.IsStatusAwaitingInsertion() &&
            p.BlockArchivesDownloaded.Peek().Index == IndexBlockArchiveQueue);

            if (peer == null)
            {
              return;
            }

            peer.SetStatusBusy();
          }
        }
      }
      

      
      async Task StartPeerGenerator()
      {
        "Start Peer generator".Log(LogFile);
        
        int countPeersToCreate = COUNT_PEERS_MAX;

        while (true)
        {
          if (countPeersToCreate > 0)
          {
            string.Format(
              "Connect with {0} new peers. " +
              "{1} peers connected currently.", 
              countPeersToCreate, 
              Peers.Count)
              .Log(LogFile);

            var createPeerTasks = new Task[countPeersToCreate];

            Parallel.For(
              0,
              countPeersToCreate,
              i => createPeerTasks[i] = CreatePeer());

            await Task.WhenAll(createPeerTasks);
          }

          lock (LOCK_Peers)
          {
            List<Peer> peersDisposed =
              Peers.FindAll(p => p.IsDisposed);

            peersDisposed.ForEach(p =>
            {
              Peers.Remove(p);
              p.Dispose();
            });

            countPeersToCreate = peersDisposed.Count;
          }

          await Task.Delay(1000).ConfigureAwait(false);
        }
      }

      async Task CreatePeer()
      {
        while (true)
        {
          IPAddress iPAddress;

          try
          {
            iPAddress = await GetNodeAddress().ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            string.Format(
              "Cannot get peer address from dns server: {0}",
              ex.Message)
              .Log(LogFile);

            await Task.Delay(5000);
            continue;
          }

          var peer = new Peer(Blockchain, iPAddress);

          try
          {
            await peer.Connect();
          }
          catch (Exception ex)
          {
            string.Format(
              "Exception {0} when syncing with peer {1}: \n{2}",
              ex.GetType(),
              peer.GetIdentification(),
              ex.Message)
              .Log(LogFile);

            peer.IsDisposed = true;

            await Task.Delay(5000);
            continue;
          }
          
          string.Format(
            "Created peer {0}", peer.GetIdentification())
            .Log(LogFile);

          peer.StartMessageListener();

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }

          return;
        }
      }

      static async Task<IPAddress> GetNodeAddress()
      {
        while (true)
        {
          lock (LOCK_IsAddressPoolLocked)
          {
            if (!IsAddressPoolLocked)
            {
              IsAddressPoolLocked = true;
              break;
            }
          }

          await Task.Delay(100);
        }

        if (SeedNodeIPAddresses.Count == 0)
        {
          DownloadIPAddressesFromSeeds();
        }

        int randomIndex = RandomGenerator
          .Next(SeedNodeIPAddresses.Count);

        IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
        SeedNodeIPAddresses.Remove(iPAddress);

        lock (LOCK_IsAddressPoolLocked)
        {
          IsAddressPoolLocked = false;
        }

        return iPAddress;
      }

      static readonly object LOCK_IsAddressPoolLocked = new object();
      static bool IsAddressPoolLocked;
      static List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();
      static Random RandomGenerator = new Random();
      
      static void DownloadIPAddressesFromSeeds()
      {
        try
        {
          string[] dnsSeeds = File.ReadAllLines(@"..\..\DNSSeeds");

          foreach (string dnsSeed in dnsSeeds)
          {
            if (dnsSeed.Substring(0, 2) == "//")
            {
              continue;
            }

            IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);

            SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList);
          }
        }
        catch
        {
          if (SeedNodeIPAddresses.Count == 0)
          {
            throw new ChainException("No seed addresses downloaded.");
          }
        }

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

          peer.StartMessageListener();

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }
        }
      }
    }
  }
}
