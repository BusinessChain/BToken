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
    partial class NetworkSynchronizer
    {
      Blockchain Blockchain;

      int TIMEOUT_SYNCHRONIZER = 30000;

      const int UTXOIMAGE_INTERVAL_SYNC = 300;
      const int UTXOIMAGE_INTERVAL_LISTEN = 5;

      StreamWriter LogFile;

      const UInt16 Port = 8333;

      const int COUNT_PEERS_MAX = 6;

      object LOCK_Peers = new object();
      List<Peer> Peers = new List<Peer>();

      static DirectoryInfo DirectoryLogPeers;
      static DirectoryInfo DirectoryLogPeersDisposed;



      public NetworkSynchronizer(Blockchain blockchain)
      {
        Blockchain = blockchain;
        
        LogFile = new StreamWriter("logSynchronizer", false);

        DirectoryLogPeers = Directory.CreateDirectory(
          "logPeers");
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
          try
          {
            if (AddressPool.Count == 0)
            {
              DownloadIPAddressesFromSeeds();

              // delete disposed log files older 24h
              // any address must not be present in either log file

              if (AddressPool.Count == 0)
              {
                break;
              }
            }

            int randomIndex = RandomGenerator
              .Next(AddressPool.Count);

            IPAddress iPAddress = AddressPool[randomIndex];
            AddressPool.Remove(iPAddress);

            lock (LOCK_Peers)
            {
              if (Peers.Any(
                p => p.IPAddress.ToString() == iPAddress.ToString()))
              {
                continue;
              }
            }

            if (iPAddresses.Any(
              ip => ip.ToString() == iPAddress.ToString()))
            {
              continue;
            }

            iPAddresses.Add(iPAddress);
          }
          catch (Exception ex)
          {
            string.Format(
              "Cannot get peer address from dns server: {0}",
              ex.Message)
              .Log(LogFile);

            break;
          }
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


      static List<IPAddress> AddressPool = new List<IPAddress>();
      static Random RandomGenerator = new Random();

      static void DownloadIPAddressesFromSeeds()
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

          try
          {
            AddressPool.AddRange(
              Dns.GetHostEntry(dnsSeed).AddressList);
          }
          catch
          {
            // If error persists, remove seed from file.
            continue;
          }
        }
      }


      async Task StartSynchronizer()
      {
        "Start Peer synchronizer.".Log(LogFile);
        
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
        Console.WriteLine("Release peer {0}", peer.GetID());
        lock (LOCK_Peers)
        {
          peer.IsBusy = false;
        }
      }


      async Task SynchronizeWithPeer(Peer peer)
      {
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

      int CountMaxDownloadsAwaiting = 10;

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

                QueueParsersInvalid.Clear();

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

          var parser = peer.BlockParser;

          if (peer == peerSyncMaster &&
            (peer.FlagDispose || peer.Command == Peer.COMMAND_NOTFOUND))
          {
            peer.FlagDispose = true;
          }
          else if (peer.FlagDispose)
          {
            Console.WriteLine(
              "Release peer {0} on line 289",
              peer.GetID());

            ReleasePeer(peer);
          }
          else if (peer.Command == Peer.COMMAND_NOTFOUND)
          {
            peer.BlockParser = new UTXOTable.BlockParser();
            peersCompleted.Add(peer);
          }
          else
          {
            if (indexBlockArchiveQueue != parser.Index)
            {
              var downloadAwaitingInsertion =
                new KeyValuePair<UTXOTable.BlockParser, Peer>(
                  parser,
                  peer);

              DownloadsAwaiting.Add(
                parser.Index,
                downloadAwaitingInsertion);

              if (!parser.IsArchiveBufferOverflow)
              {
                peer.BlockParser = Blockchain.GetBlockParser();
                
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
              }

              continue;
            }

            bool isDownloadAwaiting = false;

            if (Blockchain.TryArchiveBlocks(
              parser,
              UTXOIMAGE_INTERVAL_SYNC))
            {
            LABEL_PostProcessArchiveBlocks:

              parser.ClearPayloadData();

              if (parser.IsArchiveBufferOverflow)
              {
                parser.RecoverFromOverflow();

                RunBlockDownload(
                  peer,
                  flagContinueDownload: true);

                continue;
              }

              indexBlockArchiveQueue += 1;

              if (!isDownloadAwaiting)
              {
                if(IsBlockDownloadAvailable())
                {
                  StartBlockDownload(peer);
                }
                else
                {
                  Console.WriteLine(
                    "Release peer {0} on line 355",
                    peer.GetID());
                  
                  ReleasePeer(peer);
                }
              }
              else
              {
                Blockchain.ReleaseBlockParser(parser);
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

              parser = downloadAwaitingInsertion.Key;
              peer = downloadAwaitingInsertion.Value;

              if (Blockchain.TryArchiveBlocks(
                parser,
                UTXOIMAGE_INTERVAL_SYNC))
              {
                goto LABEL_PostProcessArchiveBlocks;
              }
            }

            peer.FlagDispose = true;

            Blockchain.ReleaseBlockParser(parser);

            if (peer != peerSyncMaster)
            {
              Console.WriteLine(
                "Release peer {0} on line 387",
                peer.GetID());

              ReleasePeer(peer);
            }
          }

          EnqueueParserInvalid(parser);

        } while (true);
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
          (QueueParsersInvalid.Count > 0 || HeaderLoad != null);
      }

      void StartBlockDownload(Peer peer)
      {
        if (QueueParsersInvalid.Any())
        {
          Blockchain.ReleaseBlockParser(peer.BlockParser);

          peer.BlockParser = QueueParsersInvalid.First();

          QueueParsersInvalid.RemoveAt(0);
        }
        else 
        {
          peer.BlockParser.SetupBlockDownload(
            IndexBlockArchiveDownload,
            ref HeaderLoad,
            peer.CountBlocksLoad);

          IndexBlockArchiveDownload += 1;
        }

        RunBlockDownload(
          peer,
          flagContinueDownload: false);
      }

      void EnqueueParserInvalid(UTXOTable.BlockParser parser)
      {
        parser.ClearPayloadData();

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
      
      async Task RunBlockDownload(
        Peer peer,
        bool flagContinueDownload)
      {
        PeersDownloading.Add(peer);

        await peer.DownloadBlocks(
          flagContinueDownload);

        QueueSynchronizer.Post(peer);
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