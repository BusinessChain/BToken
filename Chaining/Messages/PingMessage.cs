using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class PingMessage : NetworkMessage
  {
    public UInt64 Nonce { get; private set; }


    public PingMessage(NetworkMessage networkMessage) 
      : base("ping", networkMessage.Payload)
    {
      Nonce = BitConverter.ToUInt64(Payload, 0);
    }
    public PingMessage(UInt64 nonce) : base("ping")
    {
      Nonce = nonce;
      Payload = BitConverter.GetBytes(nonce);
    }
  }
}
