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
        "seed.bitcoin.jonasschnelli.ch",
        "seed.btc.petertodd.org",
        "seed.bitcoin.sprovoost.nl"
      };

      List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();

      Random RandomGenerator = new Random();


      public NetworkAddressPool()
      {
        DownloadIPAddressesFromSeeds();
      }
      void DownloadIPAddressesFromSeeds()
      {
        foreach (string dnsSeed in DnsSeeds)
        {
          IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);
          SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList);
        }

        if(SeedNodeIPAddresses.Count == 0)
        {
          throw new InvalidOperationException("No seed addresses downloaded.");
        }
      }

      public IPAddress GetRandomNodeAddress()
      {
        if (SeedNodeIPAddresses.Count == 0)
        {
          DownloadIPAddressesFromSeeds();
        }

        int randomIndex = RandomGenerator.Next(SeedNodeIPAddresses.Count);

        IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
        SeedNodeIPAddresses.Remove(iPAddress);

        return iPAddress;
      }
    }
  }
}
