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

      Header Header;

      public Header HeaderAncestor;

      public Header HeaderRoot;
      public double Difficulty;
      public int Height;

      public double DifficultyInserted;
      public int HeightInserted;
      public bool IsFork;
      public Header HeaderTipInserted;
      public List<double> HeaderDifficulties = 
        new List<double>();

      public DataArchiver Archive;


      public BranchInserter(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public void Initialize()
      {
        Header = null;

        HeaderAncestor = Blockchain.HeaderTip;

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

      
      public void AddHeaders(Header headerRoot)
      {
        if(Header != null)
        {
          if (!Header.Hash.IsEqual(headerRoot.HashPrevious))
          {
            throw new ChainException(
              "Received header does not link to last header.");
          }
        }

        Header = headerRoot;

        if (HeaderRoot == null) 
        {
          while (!HeaderAncestor.Hash.IsEqual(
            Header.HashPrevious))
          {
            Difficulty -= TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height--;

            HeaderAncestor = HeaderAncestor.HeaderPrevious;
          }

          while (HeaderAncestor.HeaderNext != null && 
            HeaderAncestor.HeaderNext.Hash.IsEqual(
              Header.Hash))
          {
            HeaderAncestor = HeaderAncestor.HeaderNext;

            Difficulty += TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height += 1;

            if (Header.HeaderNext == null)
            {
              return;
            }

            Header = Header.HeaderNext;
          }

          Header.HeaderPrevious = HeaderAncestor;

          HeaderRoot = Header;
        }

        do
        {
          // Irgendwo müssen die ungültigen Header abgeschnitten werden
          // Oder vielleicht gar nicht nötig?
          ValidateHeader();

          double difficulty = TargetManager.GetDifficulty(
            Header.NBits);
          HeaderDifficulties.Add(difficulty);

          Difficulty += difficulty;
          Height = +1;

          if (Header.HeaderNext == null)
          {
            break;
          }

          Header = Header.HeaderNext;

        } while (true);
      }
           
      void ValidateHeader()
      {
        uint medianTimePast = GetMedianTimePast(
        Header.HeaderPrevious);

        if (Header.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              Header.Hash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(Header.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
            ErrorCode.INVALID);
        }

        int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= Height &&
          Height <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format(
              "Attempt to insert header {0} at hight {1} " +
              "prior to checkpoint hight {2}",
              Header.Hash.ToHexString(),
              Height,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Checkpoints.Find(c => c.Height == Height);
        if (
          checkpoint != null &&
          !checkpoint.Hash.IsEqual(Header.Hash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              Header.Hash.ToHexString(),
              Height,
              checkpoint.Hash.ToHexString()),
            ErrorCode.INVALID);
        }

        uint targetBits = TargetManager.GetNextTargetBits(
            Header.HeaderPrevious,
            (uint)Height);

        if (Header.NBits != targetBits)
        {
          throw new ChainException(
            string.Format(
              "In header {0} nBits {1} not equal to target nBits {2}",
              Header.Hash.ToHexString(),
              Header.NBits,
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