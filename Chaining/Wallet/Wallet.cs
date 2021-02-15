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
  public class Wallet
  {
    byte[] PrivateKey = new byte[] { };
    byte[] PublicKeyHash160;

    List<UTXOTable.TXInput> TXOutputsSpendable = 
      new List<UTXOTable.TXInput>();


    public Wallet()
    {
      GeneratePublicKey(
        "6676D9347D20FEB2E5EA94DE0E6B5AFC3953DFB4C6598FEE0067645980DB7D38");
    }


    byte[] BytesLeftSideScript_P2PKH = 
      new byte[]{ 118, 169, 20};

    byte[] BytesRightSideScript_P2PKH =
      new byte[] { 136, 172 };

    const int LENGTH_P2PKH_SCRIPT = 25;
    List<int> OutputIndexesSpendable = new List<int>();

    public void DetectTXOutputsSpendable(
      int count,
      byte[] buffer,
      ref int startIndex)
    {
      int indexScript;

      for (int i = 0; i < count; i += 1)
      {
        startIndex += 8; // BYTE_LENGTH_OUTPUT_VALUE

        int lengthLockingScript = VarInt.GetInt32(
          buffer,
          ref startIndex);
        
        indexScript = startIndex;

        startIndex += lengthLockingScript;

        if (lengthLockingScript != LENGTH_P2PKH_SCRIPT)
        {
          continue;
        }

        if(!BytesLeftSideScript_P2PKH.IsEqual(
          buffer,
          indexScript))
        {
          continue;
        }

        indexScript += 3;

        if (!PublicKeyHash160.IsEqual(
          buffer,
          indexScript))
        {
          continue;
        }

        indexScript += 20;

        if(BytesRightSideScript_P2PKH.IsEqual(
          buffer,
          indexScript))
        {
          OutputIndexesSpendable.Add(i);
        }
      }
    }
    
    public void InsertTX(
      byte[] hash,
      List<UTXOTable.TXInput> inputs)
    {
      OutputIndexesSpendable.ForEach(i =>
      {
        TXOutputsSpendable.Add(
          new UTXOTable.TXInput(hash, ref i));
      });

      OutputIndexesSpendable.Clear();

      for (int i = 0; i < inputs.Count; i += 1)
      {
        if ("61a1d74df7e5f7d3ac37ea908582e5166b1fd53cd6fc15c1b510a8954b7ce62c".ToUpper()
          == inputs[i].TXIDOutput.ToHexString())
        { }

        int indexSpend = TXOutputsSpendable.FindIndex(o =>
        o.TXIDOutput.Equals(inputs[i].TXIDOutput) &&
        o.OutputIndex == inputs[i].OutputIndex);

        if (indexSpend != -1)
        {
          TXOutputsSpendable.RemoveAt(indexSpend);
        }
      }
    }
    
    public void SendAnchorToken()
    {
      if(TXOutputsSpendable.Count == 0)
      {
        return;
      }

      UTXOTable.TXInput tXInput = TXOutputsSpendable.First();
      TXOutputsSpendable.RemoveAt(0);
    }

    public void Import(Wallet wallet)
    {
      //DetectTXOutputsBeingSpent(wallet);

      TXOutputsSpendable.AddRange(
        wallet.TXOutputsSpendable);
    }

    public void Clear()
    {
      TXOutputsSpendable.Clear();
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
