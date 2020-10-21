using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
      const int COUNT_PEERS_MAX = 1;

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
          peer = Peers.Find(p => !p.IsDisposed);

          if (peer != null)
          {
            Peers.Remove(peer);
            return true;
          }

          return false;
        }
      }

      public bool TryGetPeerNotSynchronized(
        out Peer peer)
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(p =>
           !p.IsSynchronized &&
           !p.IsDisposed);
          
          if(peer != null)
          {
            Peers.Remove(peer);
            return true;
          }

          return false;
        }
      }

      public void ReleasePeer(Peer peer)
      {
        lock (LOCK_Peers)
        {
          Peers.Add(peer);
        }
      }

      public async Task Start()
      {
        "Start Network.".Log(LogFile);

        //"Start listener for inbound connection requests."
        //  .Log(LogFile);

        // StartPeerInboundListener();

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
            await peer.Connect(Port);
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

            await Task.Delay(5000);
            continue;
          }

          string.Format(
            "Created peer {0}", peer.GetID())
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
