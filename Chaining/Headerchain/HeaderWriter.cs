using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class HeaderWriter : IDisposable
      {
        Headerchain Headerchain;
        IHeaderWriter ArchiveWriter;

        public HeaderWriter(Headerchain headerchain)
        {
          Headerchain = headerchain;
          ArchiveWriter = headerchain.Archiver.GetWriter();
        }
        
        public async Task InsertHeaderAsync(NetworkHeader header)
        {
          await Headerchain.InsertHeaderAsync(header);
          ArchiveWriter.StoreHeader(header);
        }

        public void Dispose()
        {
          ArchiveWriter.Dispose();
        }
      }
    }
  }
}
