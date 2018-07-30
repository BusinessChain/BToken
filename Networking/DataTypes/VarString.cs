using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    static class VarString
    {
      public static string getString(byte[] buffer, ref int startIndex)
      {
        int stringLength = (int)VarInt.getUInt64(buffer, ref startIndex);
        string text = Encoding.ASCII.GetString(buffer, startIndex, stringLength);

        startIndex += stringLength;
        return text;
      }


      public static List<byte> getBytes(string text)
      {
        List<byte> serializedValue = VarInt.getBytes(text.Length);
        serializedValue.AddRange(Encoding.ASCII.GetBytes(text));

        return serializedValue;
      }
    }
  }
}
