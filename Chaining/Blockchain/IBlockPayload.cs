using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public interface IBlockPayload
  {
    void ParsePayload(byte[] stream);
    UInt256 GetPayloadHash();

    void StoreToDisk(string filename);
    void LoadFromDisk();
  }
}
