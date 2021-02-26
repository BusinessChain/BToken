using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Numerics;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System.Security.Cryptography;


namespace BToken.Chaining
{
  class Crypto
  {
    SHA256 SHA256 = SHA256.Create();

    string PrivKeyDec = "46345897603189110989398136884057307203509669786386043766866535737189931384120";

    public void SignatureDemo()
    {
      var publicKey = GetPubKeyFromPrivKey(PrivKeyDec);

      var message = "22c54790ad2a15b9b02bf3f12955d65a1600c26b7b9528bd07cde6b132a170bd".ToBinary();

      var signature = GetSignature(
          PrivKeyDec,
          message);

      var isvalid = VerifySignature(
        message,
        publicKey,
        signature);

      Console.WriteLine(
        "signature {0} \n is {1}",
        signature.ToHexString(),
        isvalid ? "valid" : "invalid");
    }


    bool VerifySignature(
      byte[] message,
      byte[] publicKey,
      byte[] signature)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");
      var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

      var q = curve.Curve.DecodePoint(publicKey);

      var keyParameters = new ECPublicKeyParameters(
        q,
        domain);

      ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");

      signer.Init(false, keyParameters);
      signer.BlockUpdate(message, 0, message.Length);

      return signer.VerifySignature(signature);
    }

    public void SignTX(
      string privKey,
      byte[] tX)
    {
      byte[] signature = GetSignature(
        privKey,
        tX);

      byte[] publicKey = GetPubKeyFromPrivKey(
        privKey);

      var isvalid = VerifySignature(
        tX,
        publicKey,
        signature);

      Console.WriteLine(
        "signature {0} \n is {1}",
        signature.ToHexString(),
        isvalid ? "valid" : "invalid");
    }

    byte[] GetSignature(
      string privateKey, 
      byte[] message)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");

      var domain = new ECDomainParameters(
        curve.Curve, 
        curve.G, 
        curve.N, 
        curve.H);

      Console.WriteLine(message.ToHexString());

      message = SHA256.ComputeHash(
        message,
        0,
        message.Length);

      ISigner signer = SignerUtilities
        .GetSigner("SHA-256withECDSA");
      
      var keyParameters = new ECPrivateKeyParameters(
        new Org.BouncyCastle.Math.BigInteger(privateKey),
        domain);

      while (true)
      {
        signer.Init(true, keyParameters);
        signer.BlockUpdate(message, 0, message.Length);

        byte[] signature = signer.GenerateSignature();

        if (IsSValueTooHigh(signature))
        {
          continue;
        }

        return signature;
      }
    }

    bool IsSValueTooHigh(byte[] signature)
    {
      int lengthR = signature[3];
      int msbSValue = signature[3 + lengthR + 3];

      return msbSValue > 0x7F;
    }

    byte[] GetPubKeyFromPrivKey(
      string privateKey)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");

      var domain = new ECDomainParameters(
        curve.Curve, 
        curve.G, 
        curve.N, 
        curve.H);

      var d = new Org.BouncyCastle.Math.BigInteger(privateKey);
      var q = domain.G.Multiply(d);

      var publicKey = new ECPublicKeyParameters(q, domain);
      return publicKey.Q.GetEncoded();
    }
    
  }
}
