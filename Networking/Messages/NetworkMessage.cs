
namespace BToken.Networking
{
  public class NetworkMessage
  {
    public string Command { get; protected set; }
    public byte[] Payload { get; protected set; }

    public NetworkMessage() : this("") { }
    public NetworkMessage(string command) : this(command, new byte[0]) { }
    public NetworkMessage(string command, byte[] payload)
    {
      Command = command;
      Payload = payload;
    }
  }
}
