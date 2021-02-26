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
      Crypto Crypto = new Crypto();

      string PrivKeyDec = "46345897603189110989398136884057307203509669786386043766866535737189931384120";
      byte[] PublicKeyHash160;
      
      List<TXOutputWallet> TXOutputsSpendable =
        new List<TXOutputWallet>();


      readonly byte[] PREFIX_OP_RETURN =
        new byte[] { 0x6A, 0x50 };

      byte[] PREFIX_P2PKH =
        new byte[] { 0x76, 0xA9, 0x14 };

      byte[] POSTFIX_P2PKH =
        new byte[] { 0x88, 0xAC };

      const int LENGTH_P2PKH = 25;

      public void DetectTXOutputsSpendable(TX tX)
      {
        for (int i = 0; i < tX.TXOutputs.Count; i += 1)
        {
          TXOutput tXOutput = tX.TXOutputs[i];

          if (tXOutput.LengthScript != LENGTH_P2PKH)
          {
            continue;
          }

          int indexScript = tXOutput.StartIndexScript;

          if (!PREFIX_P2PKH.IsEqual(
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

          if (POSTFIX_P2PKH.IsEqual(
            tXOutput.Buffer,
            indexScript))
          {
            byte[] scriptPubKey = new byte[LENGTH_P2PKH];
            
            Array.Copy(
              tXOutput.Buffer,
              tXOutput.StartIndexScript,
              scriptPubKey,
              0,
              LENGTH_P2PKH);

            TXOutputsSpendable.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                TXIDShort = tX.TXIDShort,
                OutputIndex = i,
                Value = tXOutput.Value,
                ScriptPubKey = scriptPubKey
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
          TXOutputsSpendable.Find(o => 
          o.TXIDShort == tXInput.TXIDOutputShort &&
          o.OutputIndex == tXInput.OutputIndex);

        if (output == null ||
          !output.TXID.IsEqual(tXInput.TXIDOutput))
        {
          return false;
        }

        TXOutputsSpendable.Remove(output);

        Console.WriteLine(
          "Spent output {0} in tx {1} with {2} satoshis.",
          output.OutputIndex,
          output.TXID.ToHexString(),
          output.Value);

        return true;
      }


      public void SendAnchorToken(
        byte[] dataOPReturn, 
        ulong fee)
      {
        TXOutputWallet outputSpendable =
          TXOutputsSpendable.Find(t => t.Value > fee);

        outputSpendable = new TXOutputWallet
        {
          TXID = "eccf7e3034189b851985d871f91384b8ee357cd47c3024736e5676eb2debb3f2".ToBinary().Reverse().ToArray(),
          OutputIndex = 1,
          ScriptPubKey = "76a914010966776006953d5567439e5e39f86a0d273bee88ac".ToBinary().Reverse().ToArray()
        };

        if (outputSpendable == null)
        {
          throw new ChainException("No spendable output found.");
        }
        
        List<byte> tXRaw = new List<byte>();

        byte[] version = { 0x01, 0x00, 0x00, 0x00 };
        tXRaw.AddRange(version);

        byte countInputs = 1;
        tXRaw.Add(countInputs);

        tXRaw.AddRange(outputSpendable.TXID);

        tXRaw.AddRange(BitConverter.GetBytes(
          outputSpendable.OutputIndex));

        tXRaw.Add(LENGTH_P2PKH);

        tXRaw.AddRange(outputSpendable.ScriptPubKey);

        byte[] sequence = { 0xFF, 0xFF, 0xFF, 0xFF };
        tXRaw.AddRange(sequence);
        
        byte countOutputs = 1; //(byte)(valueChange == 0 ? 1 : 2);
        tXRaw.Add(countOutputs);

        ulong valueChange = outputSpendable.Value - fee;
        //tXRaw.AddRange(BitConverter.GetBytes(
        //  valueChange));
        tXRaw.AddRange(BitConverter.GetBytes(
          (ulong)99900000));

        tXRaw.Add(LENGTH_P2PKH);

        tXRaw.AddRange(PREFIX_P2PKH);
        //tXRaw.AddRange(PublicKeyHash160);
        tXRaw.AddRange("097072524438d003d23a2f23edb65aae1bb3e469".ToBinary().Reverse());
        tXRaw.AddRange(POSTFIX_P2PKH);

        byte[] lockTime = new byte[4];
        tXRaw.AddRange(lockTime);

        byte[] sigHashType = { 0x01, 0x00, 0x00, 0x00 };
        tXRaw.AddRange(sigHashType);

        Crypto.SignatureDemo();

        Crypto.SignTX(
          PrivKeyDec,
          tXRaw.ToArray());

        //byte[] script = PREFIX_OP_RETURN.Concat(data).ToArray();
        //var tXOutputOPReturn =
        //  new TXOutput
        //  {
        //    Value = 0,
        //    Buffer = script,
        //    StartIndexScript = 0,
        //    LengthScript = script.Length
        //  };
      }



      public WalletUTXO()
      {
        SendAnchorToken(new byte[0], 0);

        GeneratePublicKey(
          "6676D9347D20FEB2E5EA94DE0E6B5AFC3953DFB4C6598FEE0067645980DB7D38");
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
