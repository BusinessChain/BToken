using System;

namespace BToken.Chaining
{
  public class NetworkMessage
  {
    public string Command;
    public byte[] Payload;



    public NetworkMessage(string command)
      : this(command, new byte[0])
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
