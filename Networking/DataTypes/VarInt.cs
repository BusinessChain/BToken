using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    static class VarInt
    {
      public static List<byte> getBytes(int value)
      {
        return getBytes((ulong)value);
      }
      public static List<byte> getBytes(ulong value)
      {
        List<byte> serializedValue = new List<byte>();

        byte prefix;
        int length;
        assignPrefixAndLength(value, out prefix, out length);

        serializedValue.Add(prefix);
        for (int i = 1; i < length; i++)
        {
          byte nextByte = (byte)(value >> 8 * (i - 1));
          serializedValue.Add(nextByte);
        }

        return serializedValue;
      }
      static void assignPrefixAndLength(ulong value, out byte prefix, out int length)
      {
        if (value <= 252)
        {
          prefix = (byte)value;
          length = 1;
        }
        else if (value <= 0xffff)
        {
          prefix = 0xfd;
          length = 3;
        }
        else if (value <= 0xffffffff)
        {
          prefix = 0xfe;
          length = 5;
        }
        else
        {
          prefix = 0xff;
          length = 9;
        }
      }

      public static UInt64 getUInt64(byte[] buffer, ref int startIndex)
      {
        byte prefix = buffer[startIndex];
        startIndex++;
        return convertBytesToInt(prefix, buffer, ref startIndex);
      }
      static UInt64 convertBytesToInt(UInt64 prefix, byte[] buffer, ref int startIndex)
      {
        try
        {
          if (prefix == 0xfd)
          {
            prefix = BitConverter.ToUInt16(buffer, startIndex);
            startIndex += 2;
          }
          else if (prefix == 0xfe)
          {
            prefix = BitConverter.ToUInt32(buffer, startIndex);
            startIndex += 4;
          }
          else if (prefix == 0xff)
          {
            prefix = BitConverter.ToUInt64(buffer, startIndex);
            startIndex += 8;
          }

          return prefix;

        }
        catch (ArgumentException)
        {
          throw new ArgumentException(string.Format("VarInt prefix '{0}' inconsistent with buffer length '{1}'.", prefix, buffer.Length - startIndex));
        }
      }
    }
  }
}
