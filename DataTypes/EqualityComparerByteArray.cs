using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  class EqualityComparerByteArray : IEqualityComparer<byte[]>
  {
    public bool Equals(byte[] arr1, byte[] arr2)
    {
      if (arr1.Length != arr2.Length)
      {
        return false;
      }
      for (int i = 0; i < arr1.Length; i++)
      {
        if (arr1[i] != arr2[i])
        {
          return false;
        }
      }

      return true;
    }
    public int GetHashCode(byte[] arr)
    {
      return BitConverter.ToInt32(arr, 0);
    }
  }
}
