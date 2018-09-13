using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Chaining
{
  partial class BlockchainController
  {
    abstract class BlockchainSession
    {
      protected BlockchainChannel Channel;


      public BlockchainSession() { }

      public abstract Task StartAsync(BlockchainChannel channel);
    }
  }
}
