using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class UTXOTable
    {
      class UTXOException : Exception
      {
        public UTXOException()
        {
        }

        public UTXOException(string message)
            : base(message)
        {
        }

        public UTXOException(string message, Exception inner)
            : base(message, inner)
        {
        }
      }
    }
  }
}