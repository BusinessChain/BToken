using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace BToken.Networking
{
  partial class Network
  {
    class NetworkAddressPool
    {
      List<string> DnsSeeds = new List<string>
      {
        "seed.bitcoin.sipa.be",
        "dnsseed.bluematt.me",
        "dnsseed.bitcoin.dashjr.org",
        "seed.bitcoinstats.com",
        //"seed.bitcoin.jonasschnelli.ch",
        "seed.btc.petertodd.org",
        "seed.bitcoin.sprovoost.nl"
      };

      List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();

      List<IPAddress> BlackListedIPAddresses = new List<IPAddress>();
      DateTimeOffset TimeOfLastUpdate = DateTimeOffset.UtcNow;


      Random RandomGenerator = new Random();


      public NetworkAddressPool()
      {
        DownloadIPAddressesFromSeeds();
      }
      void DownloadIPAddressesFromSeeds()
      {
        foreach (string dnsSeed in DnsSeeds)
        {
          try
          {
            IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);
            SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList);
          }
          catch
          {
            Console.WriteLine("DNS seed error :'{0}'", dnsSeed);
          }
        }

        if(SeedNodeIPAddresses.Count == 0)
        {
         throw new NetworkException("No seed addresses downloaded.");
        }
      }

      public IPAddress GetRandomNodeAddress()
      {
        UpdateBlackList();

        if (SeedNodeIPAddresses.Count == 0)
        {
          DownloadIPAddressesFromSeeds();
        }

        int randomIndex = RandomGenerator.Next(SeedNodeIPAddresses.Count);

        IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
        SeedNodeIPAddresses.Remove(iPAddress);

        if(BlackListedIPAddresses.Contains(iPAddress))
        {
          return GetRandomNodeAddress();
        }
        else
        {
          return iPAddress;

        }
      }
      void UpdateBlackList()
      {
        TimeSpan timeSinceLastUpdate = DateTimeOffset.UtcNow - TimeOfLastUpdate;
        if (timeSinceLastUpdate > TimeSpan.FromDays(1))
        {
          BlackListedIPAddresses = new List<IPAddress>();
          DateTimeOffset TimeOfLastUpdate = DateTimeOffset.UtcNow;
        }
      }

      public void Blame(IPAddress iPAddress)
      {
        BlackListedIPAddresses.Add(iPAddress);
      }
    }
  }
}
