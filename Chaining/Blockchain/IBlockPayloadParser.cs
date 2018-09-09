using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    public interface IBlockPayloadParser
    {
      IBlockPayload Parse(byte[] stream);
    }
  }
}
