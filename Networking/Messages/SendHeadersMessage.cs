using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class SendHeadersMessage : NetworkMessage
  {
    public SendHeadersMessage() : base("sendheaders") { }
  }
}
