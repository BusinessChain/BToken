using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;

namespace BToken.Accounting.Wallet
{
  class Wallet
  {
    byte[] PrivateKey = new byte[] { };
    byte[] PublicKey = new byte[] { };

    public static void GeneratePublicKey(byte[] privateKey)
    {
      var secret = BigInteger.Parse("18E14A7B6A307F426A94F8114701E7C8E774E7F9A47E2C2035DB29A206321725", NumberStyles.HexNumber);
    }
  }
}
