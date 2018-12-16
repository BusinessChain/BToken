using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public interface IPayloadParser
  {
    UInt256 GetPayloadHash(byte[] payload);
    void ValidatePayload(byte[] payload, UInt256 merkleRoot);
  }
}
