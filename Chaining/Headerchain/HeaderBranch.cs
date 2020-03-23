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
      public double AccumulatedDifficulty;
      public int Height;
      public double AccumulatedDifficultyInserted;
      public bool AreAllHeadersInserted;
      public int HeightInserted;
      public Header HeaderAncestor;



      public HeaderBranch(
        Header headerchainTip, 
        double accumulatedDifficulty,
        int height)
      {
        HeaderAncestor = headerchainTip;
        AccumulatedDifficulty = accumulatedDifficulty;
        Height = height;
      }

      public void ReportHeaderInsertion(Header header)
      {
        HeaderInsertedLast = header;

        AccumulatedDifficultyInserted +=
          HeaderDifficulties[HeightInserted++];
      }

      public void AddContainer(Header header)
      {
        if (HeaderRoot == null) 
        {
          while (!HeaderAncestor.Hash.IsEqual(
            header.HashPrevious))
          {
            AccumulatedDifficulty -= TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height--;

            HeaderAncestor = HeaderAncestor.HeaderPrevious;
          }

          while (HeaderAncestor.HeaderNext != null && 
            HeaderAncestor.HeaderNext.Hash.IsEqual(
              header.Hash))
          {
            HeaderAncestor = HeaderAncestor.HeaderNext;

            AccumulatedDifficulty += TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height += 1;

            if (header.HeaderNext == null)
            {
              return;
            }

            header = header.HeaderNext;
          }

          header.HeaderPrevious = HeaderAncestor;

          HeaderRoot = header;
          AccumulatedDifficultyInserted = AccumulatedDifficulty;
          Height =+ 1;
        }

        do
        {
          ValidateHeader(header);

          if(HeaderTip != null)
          {
            HeaderTip.HeaderNext = header;
          }
          HeaderTip = header;

          double difficulty = TargetManager.GetDifficulty(
            header.NBits);
          HeaderDifficulties.Add(difficulty);
          AccumulatedDifficulty += difficulty;

          header = header.HeaderNext;
          Height =+ 1;

        } while (header != null);
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

        int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

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
          Checkpoints.Find(c => c.Height == Height);
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
    };
  }
}