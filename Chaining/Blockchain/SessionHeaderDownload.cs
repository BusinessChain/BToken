﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Security.Cryptography;

using BToken.Networking;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class SessionHeaderDownload
    {
      Headerchain Headerchain;
      Network Network;
      INetworkChannel Channel;

      const double SECONDS_TIMEOUT_GETHEADERS = 2;

      SHA256 SHA256 = SHA256.Create();

      public SessionHeaderDownload(Headerchain headerchain, Network network)
      {
        Headerchain = headerchain;
        Network = network;
      }

      public async Task StartAsync()
      {
        Channel = await Network.RequestChannelAsync();

        List<Header> headers = await GetHeadersAsync(Headerchain.Locator.ToList());

        using (var archiveWriter = new Headerchain.HeaderWriter())
        {
          while (headers.Any())
          {
            await Headerchain.InsertHeadersAsync(archiveWriter, headers);
            headers = await GetHeadersAsync(Headerchain.Locator.ToList());
          }
        }
      }
      async Task<List<Header>> GetHeadersAsync(List<byte[]> headerLocator)
      {
        await Channel.SendMessageAsync(new GetHeadersMessage(headerLocator, Channel.GetProtocolVersion()));

        CancellationTokenSource CancellationGetHeaders = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_GETHEADERS));
        try
        {
          while (true)
          {
            NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetHeaders.Token);

            if (networkMessage.Command == "headers")
            {
              return new HeadersMessage(networkMessage, SHA256).Headers;
            }
          }
        }
        catch (OperationCanceledException ex)
        {
          Console.WriteLine("Timeout 'getheaders'");
          throw ex;
        }
      }

      public string GetSessionID()
      {
        return "Header download session";
      }
    }
  }
}
