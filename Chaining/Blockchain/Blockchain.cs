using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class Blockchain
  {
    Headerchain Headerchain;


    public Blockchain(
      Headerchain.ChainHeader genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Headerchain = new Headerchain(genesisBlock, network, checkpoints);
    }

    public void Start()
    {
      Headerchain.Start();
    }

  }
}
