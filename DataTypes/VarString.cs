using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  public static class VarString
  {
    public static string GetString(byte[] buffer, ref int startIndex)
    {
      int stringLength = VarInt.GetInt32(buffer, ref startIndex);
      string text = Encoding.ASCII.GetString(buffer, startIndex, stringLength);

      startIndex += stringLength;
      return text;
    }


    public static List<byte> GetBytes(string text)
    {
      List<byte> serializedValue = VarInt.GetBytes(text.Length);
      serializedValue.AddRange(Encoding.ASCII.GetBytes(text));

      return serializedValue;
    }
  }
}
