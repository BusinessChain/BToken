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
      public interface IHeaderArchiver
      {
        IHeaderWriter GetWriter();
        IHeaderReader GetReader();
      }

      public interface IHeaderWriter : IDisposable
      {
        void StoreHeader(NetworkHeader header);
      }
      public interface IHeaderReader : IDisposable
      {
        NetworkHeader GetNextHeader();
      }
    }
  }
}
