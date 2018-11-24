﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public interface INetwork
  {
    void PostSession(INetworkSession session);
    Task ExecuteSessionAsync(INetworkSession session);
    Task<NetworkMessage> GetMessageBlockchainAsync();
  }
}
