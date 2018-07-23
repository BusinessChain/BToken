using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class VerAckMessage : NetworkMessage
    {


      public VerAckMessage() : base("verack")
      {
      }
    }
  }
}
