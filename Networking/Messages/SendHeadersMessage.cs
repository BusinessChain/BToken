using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    class SendHeadersMessage : NetworkMessage
    {
      public SendHeadersMessage() : base("sendheaders") { }
    }
  }
}
