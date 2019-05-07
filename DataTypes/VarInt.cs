using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken
{
  public static class VarInt
  {
    public static List<byte> GetBytes(int value)
    {
      return GetBytes((ulong)value);
    }
    public static List<byte> GetBytes(ulong value)
    {
      List<byte> serializedValue = new List<byte>();

      byte prefix;
      int length;
      AssignPrefixAndLength(value, out prefix, out length);

      serializedValue.Add(prefix);
      for (int i = 1; i < length; i++)
      {
        byte nextByte = (byte)(value >> 8 * (i - 1));
        serializedValue.Add(nextByte);
      }

      return serializedValue;
    }
    static void AssignPrefixAndLength(ulong value, out byte prefix, out int length)
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
    public static Int32 ParseVarInt32(byte[] buffer, ref int index)
    {
      byte[] value = new byte[4];
      int prefix = buffer[index++];

      if (prefix == 0xfd)
      {
        Array.Copy(buffer, index, value, 0, 2);
        index += 2;
      }
      else if (prefix == 0xfe)
      {
        Array.Copy(buffer, index, value, 0, 4);
        index += 4;
      }
      else if (prefix == 0xff)
      {
        throw new ArgumentException(string.Format(
          "The VarInt in buffer at index {0} is not Int32 because it has prefix 0xFF which is Int64", index));
      }
      else
      {
        value[0] = (byte)prefix;
      }

      return BitConverter.ToInt32(value, 0);
    }
    public static UInt64 ParseVarInt(Stream stream, out int lengthVarInt)
    {
      byte[] value = new byte[8];
      int prefix = stream.ReadByte();

      if (prefix == 0xfd)
      {
        stream.Read(value, 0, 2);
        lengthVarInt = 3;
      }
      else if (prefix == 0xfe)
      {
        stream.Read(value, 0, 4);
        lengthVarInt = 5;
      }
      else if (prefix == 0xff)
      {
        stream.Read(value, 0, 8);
        lengthVarInt = 9;
      }
      else
      {
        value[0] = (byte)prefix;
        lengthVarInt = 1;
      }

      return BitConverter.ToUInt64(value, 0);
    }
    
    public static int GetInt32(byte[] buffer, ref int startIndex)
    {
      int prefix = buffer[startIndex];
      startIndex++;

      if (prefix == 0xfd)
      {
        prefix = BitConverter.ToUInt16(buffer, startIndex);
        startIndex += 2;
      }
      else if (prefix == 0xfe)
      {
        prefix = BitConverter.ToInt32(buffer, startIndex);
        startIndex += 4;
      }

      return prefix;
    }

    public static UInt64 GetUInt64(byte[] buffer, ref int startIndex)
    {
      byte prefix = buffer[startIndex];
      startIndex++;
      return ConvertBytesToLong(prefix, buffer, ref startIndex);
    }
    static UInt64 ConvertBytesToLong(UInt64 prefix, byte[] buffer, ref int startIndex)
    {
      try
      {
        if (prefix == 0xff)
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
