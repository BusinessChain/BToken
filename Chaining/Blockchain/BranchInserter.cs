using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    class BranchInserter
    {
      Blockchain Blockchain;

      public int HeightAncestor;

      public Header HeaderRoot;
      public double Difficulty;
      public int Height;
      


      
      public BranchInserter(Blockchain blockchain)
      {
        Blockchain = blockchain;
      } 
      
      
      public void Initialize(Header headerRoot)
      {
        HeaderRoot = headerRoot;
        HeightAncestor = Blockchain.Height;
        Difficulty = Blockchain.Difficulty;

        Header header = Blockchain.HeaderTip;

        while (headerRoot.HeaderPrevious != header)
        {
          Difficulty -= header.Difficulty;
          HeightAncestor -= 1;
          header = header.HeaderPrevious;
        }

        Height = HeightAncestor;
      }

      public void StageHeaders(ref Header header)
      {
        while (true)
        {
          Blockchain.ValidateHeader(header, Height + 1);
          
          Difficulty += header.Difficulty;
          Height += 1;

          if(header.HeaderNext == null)
          {
            return;
          }

          header = header.HeaderNext;
        }
      }
    }
  }
}