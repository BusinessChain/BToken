﻿using System;

namespace BToken.Networking
{
  public class NetworkMessage
  {

    protected const UInt32 ProtocolVersionLocal = Network.ProtocolVersion;

    public string Command;
    public byte[] Payload;



    public NetworkMessage(string command) 
      : this(
          command, 
          new byte[0])
    { }

    public NetworkMessage(
      string command, 
      byte[] payload)
    {
      Command = command;
      Payload = payload;
    }
  }
}
