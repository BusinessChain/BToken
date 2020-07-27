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

      public Header HeaderAncestor;
      public int HeightAncestor;

      public Header HeaderRoot;
      public Header HeaderTip;
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
      


      public void GoToHeader(Header headerRoot)
      {

        do
        {
          Difficulty -= HeaderTip.Difficulty;
          Height -= 1;
          HeaderTip = HeaderTip.HeaderPrevious;

        } while (!headerRoot.HashPrevious
        .IsEqual(HeaderTip.Hash));
      }

      public void IncrementHeaderTip()
      {
        HeaderTip = HeaderTip.HeaderNext;
        Difficulty += HeaderTip.Difficulty;
        Height += 1;
      }

      public void Initialize(
        Header headerAncestor)
      {
        HeaderAncestor = Blockchain.HeaderTip;
        HeightAncestor = Blockchain.Height;
        Difficulty = Blockchain.Difficulty;

        while (headerAncestor != HeaderAncestor)
        {
          Difficulty -= HeaderAncestor.Difficulty;
          HeightAncestor -= 1;
          HeaderAncestor = HeaderAncestor.HeaderPrevious;
        }

        IsFork = HeightAncestor < Blockchain.Height;

        Height = HeightAncestor;

        HeaderTipInserted = HeaderAncestor;
        DifficultyInserted = Difficulty;
        HeightInserted = Height;
      }

      public void StageHeaders(Header header)
      {
        do
        {
          Blockchain.ValidateHeader(header, Height + 1);

          HeaderTip = header;
          Difficulty += header.Difficulty;
          Height += 1;

          header = header.HeaderNext;
        } while (header != null);
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