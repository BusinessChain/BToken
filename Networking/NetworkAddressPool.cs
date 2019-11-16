using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;

namespace BToken.Networking
{
  partial class Network
  {
    class NetworkAddressPool
    {
      List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();
      
      DateTimeOffset TimeOfLastUpdate = DateTimeOffset.UtcNow;
      
      Random RandomGenerator = new Random();
            


      public IPAddress GetNodeAddress()
      {
        if (SeedNodeIPAddresses.Count == 0)
        {
          DownloadIPAddressesFromSeeds();
        }

        int randomIndex = RandomGenerator
          .Next(SeedNodeIPAddresses.Count);

        IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
        SeedNodeIPAddresses.Remove(iPAddress);

        return iPAddress;
      }


      void DownloadIPAddressesFromSeeds()
      {
        string[] dnsSeeds = File.ReadAllLines(@"..\..\DNSSeeds");

        foreach (string dnsSeed in dnsSeeds)
        {
          try
          {
            IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);
            SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList);
          }
          catch(Exception ex)
          {
            Console.WriteLine("DNS seed error {0}: {1}",
              dnsSeed, 
              ex.Message);
          }
        }

        if (SeedNodeIPAddresses.Count == 0)
        {
          throw new NetworkException("No seed addresses downloaded.");
        }
      }
    }
  }
}
