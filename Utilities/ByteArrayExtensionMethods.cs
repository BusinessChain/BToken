using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken
{
  static class ByteArray2HexString
  {
    public static string ToHexString(this byte[] array)
    {
      byte[] temp = (byte[])array.Clone();
      Array.Reverse(temp);
      return new SoapHexBinary(temp).ToString();
    }

    public static bool IsEqual(this byte[] arr1, byte[] arr2, int startIndex2)
    {
      for (int i = 0; i < arr1.Length; i += 1)
      {
        if (arr1[i] != arr2[startIndex2])
        {
          return false;
        }

        startIndex2 += 1;
      }

      return true;
    }

    public static bool IsEqual(this byte[] arr1, byte[] arr2)
    {
      for (int i = 0; i < arr1.Length; i += 1)
      {
        if (arr1[i] != arr2[i])
        {
          return false;
        }
      }

      return true;
    }

    public static bool IsGreaterThan(this byte[] array, uint nBits)
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

      byte[] arrayFromNBits = bytes.ToArray();

      return array.IsGreaterThan(arrayFromNBits);
    }
    public static bool IsGreaterThan(this byte[] array1, byte[] array2)
    {
      int i = array1.Length;

      while(i > array2.Length)
      {
        i -= 1;

        if (array1[i] > 0)
        {
          return true;
        }
      }

      while(i > 0)
      {
        i -= 1;

        if (array1[i] > array2[i])
        {
          return true;
        }

        if (array1[i] < array2[i])
        {
          return false;
        }
      }

      return false;
    }
  }
}
