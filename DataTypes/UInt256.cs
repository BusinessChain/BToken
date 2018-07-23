using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken
{
  public class UInt256
  {
    public const int BYTE_LENGTH = 32;

    BigInteger Data;


    UInt256(BigInteger data)
    {
      Data = data; 
    }
    public UInt256(byte[] dataBytes)
    {
      if (dataBytes.Length != BYTE_LENGTH)
      {
        throw new ArgumentException(string.Format("Length of data bytes must be '{0}', but has '{1}'", BYTE_LENGTH, dataBytes.Length));
      }
      byte[] unsignedPrefix = new byte[] { 0x00 };
      dataBytes = unsignedPrefix.Concat(dataBytes).ToArray();
      Array.Reverse(dataBytes);
      Data = new BigInteger(dataBytes);
    }
    public UInt256(string hexValue)
    {
      if(hexValue.Length != BYTE_LENGTH*2)
      {
        throw new ArgumentException(string.Format("Hex-string must have '{0}' characters, but has '{1}'", BYTE_LENGTH * 2, hexValue.Length));
      }

      string unsignedPrefix = "00";
      byte[] dataBytes = SoapHexBinary.Parse(unsignedPrefix + hexValue).Value;
      Array.Reverse(dataBytes);
      Data = new BigInteger(dataBytes);
    }

    public byte[] getBytes()
    {
      return Data.ToByteArray();
    }


    public UInt256 multiplyBy(ulong factor)
    {
      return new UInt256(Data * factor);
    }
    public UInt256 divideBy(ulong divisor)
    {
      return new UInt256(Data / divisor);
    }

    public bool isGreaterThan(UInt256 number)
    {
      return Data > number.Data;
    }
    public bool isEqual(UInt256 number)
    {
      return Data.Equals(number.Data);
    }

    public static UInt256 Max(UInt256 number1, UInt256 number2)
    {
      if(number1.isGreaterThan(number2))
      {
        return number1;
      }
      return number2;
    }

    public static explicit operator double(UInt256 d)
    {
      return (double)d.Data;
    }
  }
}
