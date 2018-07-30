using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class PingMessage : NetworkMessage
    {
      public UInt64 Nonce { get; private set; }


      public PingMessage(byte[] payload) : base("ping", payload)
      {
        Nonce = BitConverter.ToUInt64(payload, 0);
      }
      public PingMessage(UInt64 nonce) : base("ping")
      {
        Nonce = nonce;
        Payload = BitConverter.GetBytes(nonce);
      }
    }
  }
}
