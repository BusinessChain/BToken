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
          Console.WriteLine(ex.Message);
        }

        File.Delete("J:\\BlockArchivePartitioned\\0");

        LogFile = new StreamWriter("logSynchronizer", false);
      }



      public void Start()
      {
        StartPeerSynchronizer();

        StartPeerGenerator();

        // StartPeerInboundListener();
      }

                                         

      object LOCK_Peers = new object();
      List<Peer> Peers = new List<Peer>();

      async Task StartPeerSynchronizer()
      {
        try
        {
          "Start Peer synchronizer".Log(LogFile);

          Peer peer;

          while (true)
          {
            await Task.Delay(2000).ConfigureAwait(false);

            peer = null;

            lock (LOCK_Peers)
            {
              peer = Peers.Find(p =>
                !p.IsSynchronized && p.IsStatusIdle());
            }

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

            await SynchronizeWithPeer(peer);

            peer.IsSynchronized = true;

            IsBlockchainLocked = false;
          }
        }
        catch(Exception ex)
        {
          Console.WriteLine("Synchronizer crashed. \n{0}", ex.Message);
        }
      }

      async Task SynchronizeWithPeer(Peer peer)
      {
        Console.WriteLine(
          "Synchronize with peer {0}",
          peer.GetIdentification());

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
            Console.WriteLine(
              "Build headerchain from tip {0}",
              headerTip.Hash.ToHexString());

            string.Format(
              "Build headerchain from tip {0}",
              headerTip.Hash.ToHexString())
              .Log(LogFile);

            await peer.BuildHeaderchain(
              headerRoot,
              Blockchain.Height + 1);

            try
            {
              await SynchronizeUTXO(headerRoot, peer);
            }
            catch (Exception ex)
            {
              Console.WriteLine(string.Format(
                "Exception {0} when syncing with peer {1}: \n{2}",
                ex.GetType(),
                peer.GetIdentification(),
                ex.Message));

              string.Format(
                "Exception {0} when syncing with peer {1}: \n{2}",
                ex.GetType(),
                peer.GetIdentification(),
                ex.Message)
                .Log(LogFile);

              peer.IsDisposed = true;

              Blockchain.Archiver.Dispose();

              await Blockchain.LoadImage();
            }

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

            try
            {
              await SynchronizeUTXO(headerRoot, peer);
            }
            catch (Exception ex)
            {
              Console.WriteLine(string.Format(
                "Exception {0} when syncing with peer {1}: \n{2}",
                ex.GetType(),
                peer.GetIdentification(),
                ex.Message));

              string.Format(
                "Exception {0} when syncing with peer {1}: \n{2}",
                ex.GetType(),
                peer.GetIdentification(),
                ex.Message)
                .Log(LogFile);

              peer.IsDisposed = true;

              await Blockchain.LoadImage();

              return;
            }
          }
          else if (difficultyFork < difficultyOld)
          {
            if (peer.IsInbound())
            {
              Console.WriteLine("Fork weaker than Main.");

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
          Console.WriteLine(string.Format(
            "Exception {0} when syncing with peer {1}: \n{2}",
            ex.GetType(),
            peer.GetIdentification(),
            ex.Message));

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

      int CounterException = 0;

      async Task SynchronizeUTXO(
        Header headerRoot,
        Peer peerSynchronizing)
      {
        string.Format(
          "Start SynchronizeUTXO, headerRoot {0}",
          headerRoot.Hash.ToHexString())
          .Log(LogFile);

        HeaderLoad = headerRoot;
        IndexBlockArchiveDownload = 0;
        IndexBlockArchiveQueue = 0;

        Task taskUTXOSyncSessions =
          StartUTXOSyncSessions(peerSynchronizing);

        while (true)
        {
          Peer peer = await QueueSynchronizer.ReceiveAsync()
            .ConfigureAwait(false);

          UTXOTable.BlockArchive blockArchive =
            peer.BlockArchivesDownloaded.Pop();

          blockArchive.Index = Blockchain.Archiver.IndexBlockArchive;

          if(CounterException > 30)
          {
            CounterException = 0;
            throw new InvalidOperationException();
          }
          CounterException += 1;

          Console.WriteLine("Insert blockArchive {0} in synchronizer", 
            blockArchive.Index);

          Blockchain.InsertBlockArchive(blockArchive);

          Blockchain.Archiver.ArchiveBlock(
            blockArchive, UTXOIMAGE_INTERVAL_SYNC);

          Console.WriteLine("Successfully inserted blockArchive {0}",
            blockArchive.Index);

          if (blockArchive.IsLastArchive)
          {
            peer.SetUTXOSyncComplete();

            await taskUTXOSyncSessions;

            Peers.ForEach(p => p.SetStatusIdle());

            return;
          }

          RunUTXOSyncSession(peer);
        }
      }
      

      async Task StartUTXOSyncSessions(Peer peerSynchronizing)
      {
        RunUTXOSyncSession(peerSynchronizing);

        while (true)
        {
          lock (LOCK_HeaderLoad)
          {
            if (Peers.All(p => p.IsUTXOSyncComplete()))
            {
              return;
            }
          }

          var peersIdle = new List<Peer>();

          peersIdle = Peers.FindAll(p => p.IsStatusIdle());
          peersIdle.ForEach(p => p.SetStatusBusy());
          peersIdle.Select(p => RunUTXOSyncSession(p)).ToList();

          await Task.Delay(1000).ConfigureAwait(false);
        }
      }


      readonly object LOCK_HeaderLoad = new object();

      async Task RunUTXOSyncSession(Peer peer)
      {
        try
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
                peer.SetUTXOSyncComplete();
                return;
              }

              peer.BlockArchive.Index = IndexBlockArchiveDownload;
              IndexBlockArchiveDownload += 1;

              peer.CreateInventories(ref HeaderLoad);
            }

            while (!await peer.TryDownloadBlocks())
            {
              var blockArchive = peer.BlockArchive;

              Console.WriteLine(
                "Failed to download blockArchive {0} with peer {1}",
                blockArchive.Index, 
                peer.GetIdentification());

              string.Format(
                "Failed to download blockArchive {0} with peer {1}",
                blockArchive.Index,
                peer.GetIdentification())
                .Log(LogFile);

              while (true)
              {
                lock (LOCK_Peers)
                {
                  if (Peers.All(p => p.IsUTXOSyncComplete()))
                  {
                    return;
                  }

                  peer = Peers.Find(
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
            Console.WriteLine("queueu peer {0}, with blockArchive {1}",
              peer.GetIdentification(),
              peer.BlockArchive.Index);

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
        catch(Exception ex)
        {
          Console.WriteLine("Exception in session \n{0}", ex.Message);
        }
      }
      


      TcpListener TcpListener =
        new TcpListener(IPAddress.Any, Port);

      async Task StartPeerGenerator()
      {
        "Start Peer generator".Log(LogFile);
        
        int countPeersToCreate;

        while (true)
        {
          await Task.Delay(1000).ConfigureAwait(false);

          countPeersToCreate = 0;

          lock (LOCK_Peers)
          {
            List<Peer> peersDisposed =
              Peers.FindAll(p => p.IsDisposed);

            peersDisposed.ForEach(p => {
              Peers.Remove(p);
              p.Dispose();
            });

            if (Peers.Count < COUNT_PEERS_MAX)
            {
              countPeersToCreate = COUNT_PEERS_MAX - Peers.Count;

              string.Format(
                "Connected with {0} peers", Peers.Count)
                .Log(LogFile);
            }
          }

          if (countPeersToCreate > 0)
          {
            var createPeerTasks = new Task[countPeersToCreate];

            Parallel.For(
              0,
              countPeersToCreate,
              i => createPeerTasks[i] = CreatePeer());

            await Task.WhenAll(createPeerTasks);
          }
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
            Console.WriteLine(
              "Cannot get peer address from dns server: {0}",
              ex.Message);

            await Task.Delay(5000);
            continue;
          }

          var peer = new Peer(Blockchain, iPAddress);

          try
          {
            await peer.Connect().ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            Console.WriteLine(string.Format(
              "Exception {0} when syncing with peer {1}: \n{2}",
              ex.GetType(),
              peer.GetIdentification(),
              ex.Message));

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

          Console.WriteLine(
            "Created peer {0}", peer.GetIdentification());

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }

          peer.StartMessageListener();

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

      async Task StartPeerInboundListener()
      {
        TcpListener.Start(PEERS_COUNT_INBOUND);

        while (true)
        {
          TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync().
            ConfigureAwait(false);

          Console.WriteLine("Received inbound request from {0}",
            tcpClient.Client.RemoteEndPoint.ToString());

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
