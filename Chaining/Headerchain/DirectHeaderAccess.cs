using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class DirectHeaderAccess
      {
        Headerchain Headerchain;

        public DirectHeaderAccess(Headerchain headerchain)
        {
          Headerchain = headerchain;
        }

        public MemoryStream GetHeaderStream(byte[] tXID)
        {

        }
      }
    }
  }
}
