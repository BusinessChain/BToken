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
using System.Diagnostics;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class Crypto
  {
    public void SignatureDemo()
    {
      var privateKeyDec = "46345897603189110989398136884057307203509669786386043766866535737189931384120";

      var publicKey = GetPublicKeyFromPrivateKeyEx(privateKeyDec);

      var message = "22c54790ad2a15b9b02bf3f12955d65a1600c26b7b9528bd07cde6b132a170bd".ToBinary();

      var signature = GetSignature(
          privateKeyDec,
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
    

    public static bool VerifySignature(
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


    public byte[] GetSignature(string privateKey, byte[] message)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");
      var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

      var keyParameters = new ECPrivateKeyParameters(
        new Org.BouncyCastle.Math.BigInteger(privateKey),
        domain);

      ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");

      while(true)
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

    public static byte[] GetPublicKeyFromPrivateKeyEx(string privateKey)
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
    
    private static string GetPublicKeyFromPrivateKey(string privateKey)
    {
      var p = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F", NumberStyles.HexNumber);
      var b = (BigInteger)7;
      var a = BigInteger.Zero;
      var Gx = BigInteger.Parse("79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798", NumberStyles.HexNumber);
      var Gy = BigInteger.Parse("483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8", NumberStyles.HexNumber);

      CurveFp curve256 = new CurveFp(p, a, b);
      Point generator256 = new Point(curve256, Gx, Gy);

      var secret = BigInteger.Parse(privateKey, NumberStyles.HexNumber);
      var pubkeyPoint = generator256 * secret;
      return pubkeyPoint.X.ToString("X") + pubkeyPoint.Y.ToString("X");
    }
  }


  class Transaction
  {
    public string FromPublicKey { get; internal set; }
    public string ToPublicKey { get; internal set; }
    public int Amount { get; internal set; }
    public string Signature { get; internal set; }

    public override string ToString()
    {
      return $"{Amount}:{FromPublicKey}:{ToPublicKey}";
    }
  }

  /*
   * Original Source: https://bitcoin.stackexchange.com/a/25039
   */
  class Point
  {
    public static readonly Point INFINITY = new Point(null, default(BigInteger), default(BigInteger));
    public CurveFp Curve { get; private set; }
    public BigInteger X { get; private set; }
    public BigInteger Y { get; private set; }

    public Point(CurveFp curve, BigInteger x, BigInteger y)
    {
      this.Curve = curve;
      this.X = x;
      this.Y = y;
    }
    public Point Double()
    {
      if (this == INFINITY)
        return INFINITY;

      BigInteger p = this.Curve.p;
      BigInteger a = this.Curve.a;
      BigInteger l = ((3 * this.X * this.X + a) * InverseMod(2 * this.Y, p)) % p;
      BigInteger x3 = (l * l - 2 * this.X) % p;
      BigInteger y3 = (l * (this.X - x3) - this.Y) % p;
      return new Point(this.Curve, x3, y3);
    }
    public override string ToString()
    {
      if (this == INFINITY)
        return "infinity";
      return string.Format("({0},{1})", this.X, this.Y);
    }
    public static Point operator +(Point left, Point right)
    {
      if (right == INFINITY)
        return left;
      if (left == INFINITY)
        return right;
      if (left.X == right.X)
      {
        if ((left.Y + right.Y) % left.Curve.p == 0)
          return INFINITY;
        else
          return left.Double();
      }

      var p = left.Curve.p;
      var l = ((right.Y - left.Y) * InverseMod(right.X - left.X, p)) % p;
      var x3 = (l * l - left.X - right.X) % p;
      var y3 = (l * (left.X - x3) - left.Y) % p;
      return new Point(left.Curve, x3, y3);
    }
    public static Point operator *(Point left, BigInteger right)
    {
      var e = right;
      if (e == 0 || left == INFINITY)
        return INFINITY;
      var e3 = 3 * e;
      var negativeLeft = new Point(left.Curve, left.X, -left.Y);
      var i = LeftmostBit(e3) / 2;
      var result = left;
      while (i > 1)
      {
        result = result.Double();
        if ((e3 & i) != 0 && (e & i) == 0)
          result += left;
        if ((e3 & i) == 0 && (e & i) != 0)
          result += negativeLeft;
        i /= 2;
      }
      return result;
    }

    private static BigInteger LeftmostBit(BigInteger x)
    {
      BigInteger result = 1;
      while (result <= x)
        result = 2 * result;
      return result / 2;
    }
    private static BigInteger InverseMod(BigInteger a, BigInteger m)
    {
      while (a < 0) a += m;
      if (a < 0 || m <= a)
        a = a % m;
      BigInteger c = a;
      BigInteger d = m;

      BigInteger uc = 1;
      BigInteger vc = 0;
      BigInteger ud = 0;
      BigInteger vd = 1;

      while (c != 0)
      {
        BigInteger r;
        //q, c, d = divmod( d, c ) + ( c, );
        var q = BigInteger.DivRem(d, c, out r);
        d = c;
        c = r;

        //uc, vc, ud, vd = ud - q*uc, vd - q*vc, uc, vc;
        var uct = uc;
        var vct = vc;
        var udt = ud;
        var vdt = vd;
        uc = udt - q * uct;
        vc = vdt - q * vct;
        ud = uct;
        vd = vct;
      }
      if (ud > 0) return ud;
      else return ud + m;
    }
  }

  class CurveFp
  {
    public BigInteger p { get; private set; }
    public BigInteger a { get; private set; }
    public BigInteger b { get; private set; }
    public CurveFp(BigInteger p, BigInteger a, BigInteger b)
    {
      this.p = p;
      this.a = a;
      this.b = b;
    }
  }
}
