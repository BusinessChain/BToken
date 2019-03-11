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
      
    public UInt256(byte[] byteStream, ref int startIndex)
    {
      byte[] tempByteArray = new byte[BYTE_LENGTH];
      Array.Copy(byteStream, startIndex, tempByteArray, 0, BYTE_LENGTH);
      startIndex += BYTE_LENGTH;

      WriteToInternalData(tempByteArray);
    }
    public UInt256(byte[] dataBytes)
    {
      WriteToInternalData(dataBytes);
    }
    public UInt256(string hexValue)
    {
      byte[] dataBytes = SoapHexBinary.Parse(hexValue).Value;
      Array.Reverse(dataBytes);

      WriteToInternalData(dataBytes);
    }

    void WriteToInternalData(byte[] dataBytes)
    {
      Array.Resize(ref dataBytes, BYTE_LENGTH);
      AddUnsignedPostfix(ref dataBytes);
      Data = new BigInteger(dataBytes);
    }
    static void AddUnsignedPostfix(ref byte[] dataBytes)
    {
      byte[] unsignedPostfix = new byte[] { 0x00 };
      dataBytes = dataBytes.Concat(unsignedPostfix).ToArray();
    }


    public byte[] GetBytes()
    {
      byte[] byteArray = Data.ToByteArray();
      Array.Resize(ref byteArray, BYTE_LENGTH);
      return byteArray;
    }

    public UInt32 GetCompact()
    {
      uint numberOfBytesUsed;

      UInt32 compact = 0;

      compact |= GetMantissa(GetBytes(), out numberOfBytesUsed);
      compact |= numberOfBytesUsed << 24;

      return compact;
    }
    static uint GetMantissa(byte[] bytes, out uint numberOfBytesUsed)
    {
      uint numberOfBytesUnused = 0;
      uint mantissa = 0;

      for (int i = bytes.Length - 1; i >= 0; i--)
      {
        if (bytes[i] == 0x00)
        {
          numberOfBytesUnused++;
        }
        else
        {
          mantissa = ParseMantissa(bytes, i);
          break;
        }
      }

      numberOfBytesUsed = (uint)bytes.Length - numberOfBytesUnused;

      if ((mantissa & 0x00800000) != 0)
      {
        mantissa >>= 8;
        numberOfBytesUsed++;
      }

      return mantissa;
    }
    static uint ParseMantissa(byte[] bytes, int startindex)
    {
      uint mantissa = 0;

      int MANTISSA_SIZE = 3;
      for (int m = 0; m < MANTISSA_SIZE; m++)
      {
        mantissa <<= 8;
        mantissa |= bytes[startindex - m];
      }

      return mantissa;
    }

    public static UInt256 ParseFromCompact(uint nBits)
    {
      int expBits = ((int)nBits & 0x7F000000) >> 24;
      UInt32 factorBits = nBits & 0x00FFFFFF;

      if (expBits < 3)
      {
        factorBits >>= (3 - expBits) * 8;
      }

      var bytes = new List<byte>();

      for (int i = expBits - 3; i > 0; i--)
      {
        bytes.Add(0x00);
      }
      bytes.Add((byte)(factorBits & 0xFF));
      bytes.Add((byte)((factorBits & 0xFF00) >> 8));
      bytes.Add((byte)((factorBits & 0xFF0000) >> 16));

      return new UInt256(bytes.ToArray());
    }

    public override string ToString()
    {
      byte[] dataBytes = GetBytes();
      Array.Reverse(dataBytes);
      SoapHexBinary soapHexBinary = new SoapHexBinary(dataBytes);
      return soapHexBinary.ToString();
    }

    public UInt256 MultiplyBy(ulong factor)
    {
      return new UInt256(Data * factor);
    }
    public UInt256 DivideBy(ulong divisor)
    {
      return new UInt256(Data / divisor);
    }

    public bool IsGreaterThan(UInt256 number)
    {
      return Data > number.Data;
    }

    public static UInt256 Min(UInt256 number1, UInt256 number2)
    {
      if (number1.IsGreaterThan(number2))
      {
        return number2;
      }
      return number1;
    }

    public override bool Equals(object obj)
    {
      UInt256 uInt256 = obj as UInt256;

      if (uInt256 == null) { return false; }
      return Data.Equals(uInt256.Data);
    }
    public override int GetHashCode()
    {
      return Data.GetHashCode();
    }

    public static explicit operator double(UInt256 d)
    {
      return (double)d.Data;
    }

  }
}
