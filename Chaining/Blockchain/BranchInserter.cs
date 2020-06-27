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

      public Header HeaderTip;
      public double Difficulty;
      public int Height;

      public Header HeaderRoot;
      public int HeightAncestor;
           
      public double DifficultyInserted;
      public int HeightInserted;
      public bool IsFork;
      public Header HeaderTipInserted;
      public List<double> HeaderDifficulties = 
        new List<double>();
      
      public BlockArchiver Archive;




      public BranchInserter()
      { }

      public BranchInserter(Blockchain blockchain)
      {
        Blockchain = blockchain;

        Initialize();
      }

      public void Initialize()
      {
        HeaderAncestor = Blockchain.HeaderTip;

        HeaderTip = null;
        HeaderRoot = null;
        Difficulty = Blockchain.Difficulty;
        Height = Blockchain.HeightStagedInserted;

        DifficultyInserted = Difficulty;
        HeightInserted = Height;

        IsFork = false;

        HeaderDifficulties.Clear();

        Archive.Branch(Blockchain.Archive);
      }


      public void ReportBlockInsertion(Header header)
      {
        HeaderTipInserted = header;

        HeightInserted += 1;

        DifficultyInserted += 
          HeaderDifficulties[HeightInserted];
      }
      
      public async Task Stage(BlockchainPeer peer)
      {
        HeaderTip = Blockchain.HeaderTip;
        Height = Blockchain.Height;
        Difficulty = Blockchain.Difficulty;

        List<byte[]> locator = GetLocatorHashes(HeaderTip);

        try
        {
          UTXOTable.BlockArchive archiveBlock =
            await peer.GetHeaders(locator);

          if (archiveBlock.Height == 0)
          {
            return;
          }

          IsFork = !archiveBlock.HeaderRoot.HashPrevious
            .IsEqual(HeaderTip.Hash);

          if (IsFork)
          {
            while (!archiveBlock.HeaderRoot.HashPrevious
              .IsEqual(HeaderTip.Hash))
            {
              Difficulty -= HeaderTip.Difficulty;
              Height -= 1;
              HeaderTip = HeaderTip.HeaderPrevious;
            }

            while (archiveBlock.HeaderRoot.Hash.IsEqual(
                HeaderTip.HeaderNext.Hash))
            {
              HeaderTip = HeaderTip.HeaderNext;
              Difficulty += HeaderTip.Difficulty;
              Height += 1;

              archiveBlock.Difficulty -= archiveBlock.HeaderRoot.Difficulty;
              archiveBlock.Height -= 1;
              archiveBlock.HeaderRoot = archiveBlock.HeaderRoot.HeaderNext;

              if (archiveBlock.HeaderRoot == null)
              {
                archiveBlock = await peer.GetHeaders(locator);
              }
            }

            HeightAncestor = Height;
          }

          archiveBlock.HeaderRoot.HeaderPrevious = HeaderTip;

          Blockchain.ValidateHeaders(archiveBlock.HeaderRoot);

          HeaderRoot = archiveBlock.HeaderRoot;
          
          HeaderTipInserted = HeaderTip;
          DifficultyInserted = Difficulty;
          HeightInserted = Height;

          StageHeaders(archiveBlock);

          while (true)
          {
            archiveBlock = await peer.GetHeaders(locator);

            if (archiveBlock.Height == 0)
            {
              return;
            }

            archiveBlock.HeaderRoot.HeaderPrevious = HeaderTip;

            Blockchain.ValidateHeaders(archiveBlock.HeaderRoot);

            HeaderTip.HeaderNext = archiveBlock.HeaderRoot;

            StageHeaders(archiveBlock);
          }
        }
        catch (Exception ex)
        {
          peer.Dispose(string.Format(
            "Exception {0} when syncing: \n{1}",
            ex.GetType(),
            ex.Message));
        }
      }

      public bool ContinueSkipDuplicates(
        UTXOTable.BlockArchive archiveBlock)
      {
        while (archiveBlock.HeaderRoot.Hash.IsEqual(
            HeaderTip.HeaderNext.Hash))
        {
          HeaderTip = HeaderTip.HeaderNext;
          Difficulty += HeaderTip.Difficulty;
          Height += 1;

          archiveBlock.Difficulty -= archiveBlock.HeaderRoot.Difficulty;
          archiveBlock.Height -= 1;
          archiveBlock.HeaderRoot = archiveBlock.HeaderRoot.HeaderNext;

          if (archiveBlock.HeaderRoot.HeaderNext == null)
          {
            return true;
          }
        }

        return false;
      }

      void StageHeaders(UTXOTable.BlockArchive archiveBlock)
      {
        HeaderTip = archiveBlock.HeaderTip;
        Difficulty += archiveBlock.Difficulty;
        Height += archiveBlock.Height;
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

      void ValidateHeader(Header header)
      {
        uint medianTimePast = GetMedianTimePast(
        header.HeaderPrevious);

        if (header.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              header.Hash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
            ErrorCode.INVALID);
        }

        int hightHighestCheckpoint = Blockchain
          .Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= Height &&
          Height <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format(
              "Attempt to insert header {0} at hight {1} " +
              "prior to checkpoint hight {2}",
              header.Hash.ToHexString(),
              Height,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Blockchain.Checkpoints.Find(c => c.Height == Height);
        if (
          checkpoint != null &&
          !checkpoint.Hash.IsEqual(header.Hash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              header.Hash.ToHexString(),
              Height,
              checkpoint.Hash.ToHexString()),
            ErrorCode.INVALID);
        }

        uint targetBits = TargetManager.GetNextTargetBits(
            header.HeaderPrevious,
            (uint)Height);

        if (header.NBits != targetBits)
        {
          throw new ChainException(
            string.Format(
              "In header {0} nBits {1} not equal to target nBits {2}",
              header.Hash.ToHexString(),
              header.NBits,
              targetBits),
            ErrorCode.INVALID);
        }
      }

      static uint GetMedianTimePast(Header header)
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();

        int depth = 0;
        while (depth < MEDIAN_TIME_PAST)
        {
          timestampsPast.Add(header.UnixTimeSeconds);

          if (header.HeaderPrevious == null)
          { break; }

          header = header.HeaderPrevious;
          depth++;
        }

        timestampsPast.Sort();

        return timestampsPast[timestampsPast.Count / 2];
      }

    }
  }
}