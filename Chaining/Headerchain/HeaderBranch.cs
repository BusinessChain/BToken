using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    public class HeaderBranch
    {
      public Header HeaderRoot;
      public Header HeaderTip;
      public Header HeaderInsertedLast;
      public List<double> HeaderDifficulties = new List<double>();
      public double Difficulty;
      public int Height;
      public double DifficultyInserted;
      public int HeightInserted;
      public Header HeaderAncestor;



      public HeaderBranch(
        Header headerchainTip, 
        double accumulatedDifficulty,
        int height)
      {
        HeaderAncestor = headerchainTip;
        Difficulty = accumulatedDifficulty;
        Height = height;
      }

      public void ReportHeaderInsertion(Header header)
      {
        HeaderInsertedLast = header;

        DifficultyInserted +=
          HeaderDifficulties[HeightInserted++];
      }


      Header Header;

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
          DifficultyInserted = Difficulty;
          Height =+ 1;
        }

        do
        {
          // Irgendwo müssen die ungültigen Header abgeschnitten werden
          // Oder vielleicht gar nicht nötig?
          ValidateHeader();

          if(HeaderTip != null)
          {
            HeaderTip.HeaderNext = Header;
          }
          HeaderTip = Header;

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
    };
  }
}