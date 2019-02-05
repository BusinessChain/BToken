using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;

using BToken.Hashing;

namespace BToken.Accounting
{
  class Wallet
  {
    UTXO UTXO;

    byte[] PrivateKey = new byte[] { };
    byte[] PublicKey = new byte[] { };


    public Wallet(UTXO uTXO)
    {
      UTXO = uTXO;
    }

    public static void GeneratePublicKey()
    {
      var secret = BigInteger.Parse(
        "18E14A7B6A307F426A94F8114701E7C8E774E7F9A47E2C2035DB29A206321725", 
        NumberStyles.HexNumber);

      SECP256K1.ECPoint publicKey = SECP256K1.GeneratePublicKey(secret);
      
      Console.WriteLine("pubkey:\n{0}\n{1}", publicKey.X.ToString("X"), publicKey.Y.ToString("X"));
    }
  }
}
