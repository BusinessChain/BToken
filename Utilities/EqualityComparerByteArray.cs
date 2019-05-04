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
      return arr1.IsEqual(arr2);
    }
    public int GetHashCode(byte[] arr)
    {
      return BitConverter.ToInt32(arr, 0);
    }
  }
}
