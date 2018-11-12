using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    public interface IPayloadParser
    {
      IBlockPayload Parse(byte[] stream);
      UInt256 GetPayloadHash(byte[] payload);
      void ValidatePayload();
    }
  }
}
