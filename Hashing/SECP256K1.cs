using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;

namespace BToken.Hashing
{
  public static class SECP256K1
  {
    static readonly BigInteger P = BigInteger.Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F", NumberStyles.HexNumber);
    static readonly BigInteger B = 7;
    static readonly BigInteger A = BigInteger.Zero;
    static readonly BigInteger Gx = BigInteger.Parse("79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798", NumberStyles.HexNumber);
    static readonly BigInteger Gy = BigInteger.Parse("483ada7726a3c4655da4fbfc0e1108a8fd17b448a68554199c47d08ffb10d4b8", NumberStyles.HexNumber);
    static readonly ECPoint G = new ECPoint(Gx, Gy);

    public static ECPoint GeneratePublicKey(BigInteger privateKey)
    {
      return G * privateKey;
    }

    public class ECPoint
    {
      public static readonly ECPoint INFINITY = new ECPoint(default, default);
      public BigInteger X { get; private set; }
      public BigInteger Y { get; private set; }

      public ECPoint(BigInteger x, BigInteger y)
      {
        X = x;
        Y = y;
      }
      public ECPoint Double()
      {
        if (this == INFINITY)
          return INFINITY;

        BigInteger p = P;
        BigInteger a = A;
        BigInteger l = (3 * X * X + a) * InverseMod(2 * Y, p) % p;
        BigInteger x3 = (l * l - 2 * X) % p;
        BigInteger y3 = (l * (X - x3) - Y) % p;
        return new ECPoint(x3, y3);
      }
      public override string ToString()
      {
        if (this == INFINITY)
          return "infinity";
        return string.Format("({0},{1})", X, Y);
      }
      public static ECPoint operator +(ECPoint a, ECPoint b)
      {
        if (b == INFINITY)
          return a;
        if (a == INFINITY)
          return b;
        if (a.X == b.X)
        {
          if ((a.Y + b.Y) % P == 0)
            return INFINITY;
          else
            return a.Double();
        }

        var p = P;
        var l = ((b.Y - a.Y) * InverseMod(b.X - a.X, p)) % p;
        var x3 = (l * l - a.X - b.X) % p;
        var y3 = (l * (a.X - x3) - a.Y) % p;
        return new ECPoint(x3, y3);
      }
      public static ECPoint operator *(ECPoint point, BigInteger scalar)
      {
        if (scalar == 0 || point == INFINITY)
          return INFINITY;
        var scalarTimes3 = 3 * scalar;
        var negativePoint = new ECPoint(point.X, -point.Y);
        var i = LeftmostBit(scalarTimes3) / 2;
        var result = point;
        while (i > 1)
        {
          result = result.Double();
          if ((scalarTimes3 & i) != 0 && (scalar & i) == 0)
            result += point;
          if ((scalarTimes3 & i) == 0 && (scalar & i) != 0)
            result += negativePoint;
          i /= 2;
        }
        return result;
      }

      static BigInteger LeftmostBit(BigInteger x)
      {
        BigInteger result = 1;
        while (result <= x)
          result = 2 * result;
        return result / 2;
      }
      static BigInteger InverseMod(BigInteger a, BigInteger m)
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
          var q = BigInteger.DivRem(d, c, out r);
          d = c;
          c = r;

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
  }
}
