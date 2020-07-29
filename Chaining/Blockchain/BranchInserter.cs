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

      public Header HeaderTipInserted;
      public double DifficultyInserted;
      public int HeightInserted;

      public bool IsFork;
      


      
      public BranchInserter(Blockchain blockchain)
      {
        Blockchain = blockchain;
      } 
      
      
      public void Initialize(
        Header headerRoot)
      {
        HeaderRoot = headerRoot;
        HeightAncestor = Blockchain.Height;
        Difficulty = Blockchain.Difficulty;

        Header headerAncestor = Blockchain.HeaderTip;

        while (headerRoot.HeaderPrevious != headerAncestor)
        {
          Difficulty -= headerAncestor.Difficulty;
          HeightAncestor -= 1;
          headerAncestor = headerAncestor.HeaderPrevious;
        }

        IsFork = HeightAncestor < Blockchain.Height;

        Height = HeightAncestor;

        HeaderTipInserted = headerAncestor;
        DifficultyInserted = Difficulty;
        HeightInserted = HeightAncestor;
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

      public void InsertHeaders(UTXOTable.BlockArchive archiveBlock)
      {
        HeaderTipInserted = archiveBlock.HeaderTip;
        DifficultyInserted += archiveBlock.Difficulty;
        HeightInserted += archiveBlock.Height;
      }
      
      public void Commit()
      {
        HeaderRoot.HeaderPrevious.HeaderNext = HeaderRoot;

        Blockchain.HeaderTip = HeaderTipInserted;
        Blockchain.Difficulty = DifficultyInserted;
        Blockchain.Height = HeightInserted;
      }
    }
  }
}