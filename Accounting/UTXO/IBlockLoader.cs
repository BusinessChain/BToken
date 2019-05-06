using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    interface IBlockLoader
    {
      List<Block> GetBlocks();
      Headerchain.ChainHeader GetChainHeader();
    }
  }
}
