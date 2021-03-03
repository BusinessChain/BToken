using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class Block
  {
    public byte[] Buffer;

    public Header Header;
    public List<UTXOTable.TX> TXs;


    public Block(
      byte[] buffer, 
      Header header, 
      List<UTXOTable.TX> tXs)
    {
      Buffer = buffer;
      Header = header;

      TXs = tXs;
    }
  }
}
