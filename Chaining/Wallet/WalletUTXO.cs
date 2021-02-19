using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;
using System.Security.Cryptography;

using BToken.Hashing;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class WalletUTXO
    {
      byte[] PrivateKey = new byte[] { };
      byte[] PublicKeyHash160;

      List<TXInput> TXOutputsSpendable =
        new List<TXInput>();


      public WalletUTXO()
      {
        GeneratePublicKey(
          "6676D9347D20FEB2E5EA94DE0E6B5AFC3953DFB4C6598FEE0067645980DB7D38");
      }


      byte[] BytesLeftSideScript_P2PKH =
        new byte[] { 118, 169, 20 };

      byte[] BytesRightSideScript_P2PKH =
        new byte[] { 136, 172 };

      const int LENGTH_P2PKH_SCRIPT = 25;

      List<TXOutputWallet> TXOutputSpendable = 
        new List<TXOutputWallet>();
      
      public void DetectTXOutputsSpendable(TX tX)
      {
        for (int i = 0; i < tX.TXOutputs.Count; i += 1)
        {
          TXOutput tXOutput = tX.TXOutputs[i];

          if (tXOutput.LengthScript != LENGTH_P2PKH_SCRIPT)
          {
            continue;
          }

          int indexScript = tXOutput.StartIndexScript;

          if (!BytesLeftSideScript_P2PKH.IsEqual(
            tXOutput.Buffer,
            indexScript))
          {
            continue;
          }

          indexScript += 3;

          if (!PublicKeyHash160.IsEqual(
            tXOutput.Buffer,
            indexScript))
          {
            continue;
          }

          indexScript += 20;

          if (BytesRightSideScript_P2PKH.IsEqual(
            tXOutput.Buffer,
            indexScript))
          {
            TXOutputSpendable.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                TXIDShort = tX.TXIDShort,
                OutputIndex = i,
                Value = tXOutput.Value
              });

            Console.WriteLine(
              "Detected spendable output {0} " +
              "in tx {1} with {2} satoshis.",
              i,
              tX.Hash.ToHexString(),
              tXOutput.Value);
          }
        }
      }

      public bool TrySpend(TXInput tXInput)
      {
        TXOutputWallet output = 
          TXOutputSpendable.Find(o => 
          o.TXIDShort == tXInput.TXIDOutputShort &&
          o.OutputIndex == tXInput.OutputIndex);

        if (output == null ||
          !output.TXID.IsEqual(tXInput.TXIDOutput))
        {
          return false;
        }

        TXOutputSpendable.Remove(output);

        Console.WriteLine(
          "Spent output {0} in tx {1} with {2} satoshis.",
          output.OutputIndex,
          output.TXID.ToHexString(),
          output.Value);

        return true;
      }

      public void SendAnchorToken()
      {
        if (TXOutputsSpendable.Count == 0)
        {
          return;
        }

        TXInput tXInput = TXOutputsSpendable.First();
        TXOutputsSpendable.RemoveAt(0);
      }

      void GeneratePublicKey(string privKey)
      {
        var secret = BigInteger.Parse(
          privKey,
          NumberStyles.HexNumber);

        SECP256K1.ECPoint publicKey =
          SECP256K1.GeneratePublicKey(secret);

        var publicKeyX = publicKey.X.ToByteArray().Take(32).Reverse().ToArray();
        var publicKeyY = publicKey.Y.ToByteArray().Take(32).Reverse().ToArray();

        SHA256 sHA256 = SHA256.Create();
        RIPEMD160 rIPEMD160 = RIPEMD160.Create();

        var b1 = new byte[1] { 0x04 }.Concat(
          publicKeyX.Concat(publicKeyY))
          .ToArray();

        PublicKeyHash160 = rIPEMD160.ComputeHash(
          sHA256.ComputeHash(b1));
      }
    }
  }
}
