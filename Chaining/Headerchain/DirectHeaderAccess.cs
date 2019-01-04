using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class DirectHeaderAccess
      {
        Headerchain Headerchain;
        Dictionary<byte[], List<ChainHeader>> HeaderIndex;

        public DirectHeaderAccess(Headerchain headerchain)
        {
          Headerchain = headerchain;
          HeaderIndex = new Dictionary<byte[], List<ChainHeader>>(new EqualityComparerByteArray());
        }

        public void Update()
        {
          try
          {
            byte[] keyHeader = Headerchain.MainChain.HeaderTipHash.GetBytes().Take(4).ToArray();
            ChainHeader header = Headerchain.MainChain.HeaderTip;

            if (!HeaderIndex.TryGetValue(keyHeader, out List<ChainHeader> headers))
            {
              headers = new List<ChainHeader>();
              HeaderIndex.Add(keyHeader, headers);
            }
            headers.Add(header);
          }
          catch(Exception ex)
          {
            Console.WriteLine("Failed to update DirectHeaderAccess: " + ex.Message);
          }
        }

        class EqualityComparerByteArray : IEqualityComparer<byte[]>
        {
          public bool Equals(byte[] arr1,byte[] arr2)
          {
            if(arr1.Length != arr2.Length)
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
          public int GetHashCode(byte[] arr1)
          {
            return BitConverter.ToInt32(arr1, 0);
          }
        }
      }
    }
  }
}
