﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace BToken.Networking
{
  public interface INetworkChannel : IDisposable
  {
    List<NetworkMessage> GetApplicationMessages();

    Task<byte[]> GetHeaders(
      IEnumerable<byte[]> locatorHashes, 
      CancellationToken cancellationToken);

    Task SendMessage(NetworkMessage networkMessage);

    Task<NetworkMessage> ReceiveApplicationMessage(
        CancellationToken cancellationToken);

    string GetIdentification();
  }
}
