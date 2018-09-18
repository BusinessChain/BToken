using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public interface IBlockPayload
  {
    void ParsePayload(byte[] stream);
    UInt256 GetPayloadHash();

    void StoreToDisk(NetworkHeader header, string filename);
    void LoadFromDisk();
  }
}
