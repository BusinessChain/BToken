using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  class EqualityComparerIntegerArray : IEqualityComparer<int[]>
  {
    public bool Equals(int[] arr1, int[] arr2)
    {
      return IsEqual(arr1, arr2);
    }
    public int GetHashCode(int[] arr)
    {
      return arr[0];
    }

    public static bool IsEqual(int[] arr1, int[] arr2)
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
  }
}
