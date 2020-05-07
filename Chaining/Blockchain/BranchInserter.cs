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

      public Header HeaderTip;
      public Header HeaderRoot;
      public double Difficulty;
      public int Height;

      public double DifficultyInserted;
      public int HeightInserted;
      public bool IsFork;
      public Header HeaderTipInserted;
      public List<double> HeaderDifficulties = 
        new List<double>();

      public BlockArchiver Archive;


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
        Height = Blockchain.Height;

        DifficultyInserted = Difficulty;
        HeightInserted = Height;

        IsFork = false;

        HeaderDifficulties.Clear();

        Archive.Branch(Blockchain.Archive);
      }

      public async Task LoadHeaders(
        BlockchainPeer peer)
      {
        List<byte[]> locator = Blockchain.Locator.Locations
          .Select(b => b.Hash)
          .ToList();

        try
        {
          Header header = await peer.GetHeaders(locator);

          while (header != null)
          {
            AddHeaders(header);
            header = await peer.GetHeaders(locator);
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

      public void ReportBlockInsertion(Header header)
      {
        HeaderTipInserted = header;

        HeightInserted += 1;

        DifficultyInserted += 
          HeaderDifficulties[HeightInserted];
      }

      
      public void AddHeaders(Header header)
      {
        if (HeaderRoot == null) 
        {
          while (!HeaderAncestor.Hash.IsEqual(
            header.HashPrevious))
          {
            Difficulty -= TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height--;

            HeaderAncestor = HeaderAncestor.HeaderPrevious;
          }

          while (HeaderAncestor.HeaderNext != null && 
            HeaderAncestor.HeaderNext.Hash.IsEqual(
              header.Hash))
          {
            HeaderAncestor = HeaderAncestor.HeaderNext;

            Difficulty += TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height += 1;

            if (header.HeaderNext == null)
            {
              return;
            }

            header = header.HeaderNext;
          }

          HeaderRoot = header;
          HeaderRoot.HeaderPrevious = HeaderAncestor;
        }

        if (HeaderTip != null)
        {
          if (!HeaderTip.Hash.IsEqual(header.HashPrevious))
          {
            throw new ChainException(
              "Received header does not link to last header.");
          }
        }

        while (header != null)
        {
          ValidateHeader(header);

          double difficulty = TargetManager.GetDifficulty(header.NBits);
          HeaderDifficulties.Add(difficulty);
          Difficulty += difficulty;
          Height = +1;

          HeaderTip = header;

          header = header.HeaderNext;
        }
      }

      public void InsertHeaders(Header header)
      {
        AddHeaders(header);
        Blockchain.InsertBranch();
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