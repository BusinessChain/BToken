using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Network
    {
      const UInt16 Port = 8333;

      Blockchain Blockchain;
      const int COUNT_PEERS_MAX = 6;

      object LOCK_Peers = new object();
      List<Peer> Peers = new List<Peer>();

      StreamWriter LogFile;



      public Network(Blockchain blockchain)
      {
        Blockchain = blockchain;
        LogFile = new StreamWriter("logNetwork", false);
      }


      public bool TryGetPeer(
        out Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(
            p => !p.FlagDispose && !p.IsBusy);

          if (peer != null)
          {
            peer.IsBusy = true;
            return true;
          }
        }

        return false;
      }

      public bool TryGetPeerNotSynchronized(
        out Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(p =>
           !p.IsSynchronized &&
           !p.FlagDispose &&
           !p.IsBusy);
          
          if(peer != null)
          {
            peer.IsBusy = true;
            return true;
          }

          return false;
        }
      }

      public void ReleasePeer(Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer.IsBusy = false;
        }
      }

      // verhindern dass mit demselben peer zweimal verbunden wird.
      public async Task Start()
      {
        "Start Network.".Log(LogFile);

        //"Start listener for inbound connection requests."
        //  .Log(LogFile);

        // StartPeerInboundListener();

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
              "Connect with {0} new peers.\n" +
              "{1} peers connected currently.",
              countPeersToCreate,
              Peers.Count)
              .Log(LogFile);

            List<IPAddress> iPAddresses = 
              RetrieveIPAddresses(countPeersToCreate);

            if(iPAddresses.Count > 0)
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
        bool flagPolledSeedServer = false;

        while(iPAddresses.Count < countMax)
        {
          try
          {
            if (SeedNodeIPAddresses.Count == 0)
            {
              if(flagPolledSeedServer)
              {
                break;
              }

              DownloadIPAddressesFromSeeds();
              flagPolledSeedServer = true;
            }

            if(SeedNodeIPAddresses.Count == 0)
            {
              break;
            }

            int randomIndex = RandomGenerator
              .Next(SeedNodeIPAddresses.Count);

            IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
            SeedNodeIPAddresses.Remove(iPAddress);
            
            lock(LOCK_Peers)
            {
              if(Peers.Any(
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


      static List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();
      static Random RandomGenerator = new Random();

      static void DownloadIPAddressesFromSeeds()
      {
        string pathFileSeeds = @"..\..\DNSSeeds";
        string[] dnsSeeds;

        while(true)
        {
          try
          {
            dnsSeeds = File.ReadAllLines(pathFileSeeds);

            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "Exception {0} when reading file with DNS seeds {1} \n" +
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
            SeedNodeIPAddresses.AddRange(
              Dns.GetHostEntry(dnsSeed).AddressList);
          }
          catch
          {
            // If error persists, remove seed from file.
            continue;
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
