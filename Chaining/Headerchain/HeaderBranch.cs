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
      public bool IsFork;
      public Header HeaderForkTip;
      public bool IsForkTipInserted;
      public bool IsHeaderTipInserted;
      public Header HeaderRoot;
      public Header HeaderTip;
      public Header HeaderLastInserted;
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
        HeaderLastInserted = header;

        if (header == HeaderForkTip)
        {
          IsForkTipInserted = true;
        }

        if (header == HeaderTip)
        {
          IsHeaderTipInserted = true;
        }

        AccumulatedDifficultyInserted +=
          HeaderDifficulties[HeightInserted++];
      }

      public void AddContainer(HeaderContainer container)
      {
        if (HeaderRoot == null) 
        {
          while (!container.HeaderRoot.HashPrevious.IsEqual(
            HeaderAncestor.Hash))
          {
            AccumulatedDifficulty -= TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height--;

            HeaderAncestor = HeaderAncestor.HeaderPrevious;
          }

          while (HeaderAncestor.HeaderNext.Hash
            .IsEqual(container.HeaderRoot.Hash))
          {
            HeaderAncestor = HeaderAncestor.HeaderNext;

            AccumulatedDifficulty += TargetManager.GetDifficulty(
              HeaderAncestor.NBits);

            Height++;

            if (container.HeaderRoot.HeaderNext == null)
            {
              return;
            }

            container.HeaderRoot = container.HeaderRoot.HeaderNext;
          }
          
          HeaderRoot = container.HeaderRoot;
          HeaderRoot.HeaderPrevious = HeaderAncestor;
        }

        Header headerNext = container.HeaderRoot;
        int heightNext = Height + 1;

        do
        {
          ValidateHeaderNext(
            headerNext, 
            heightNext);

          if(HeaderTip != null)
          {
            HeaderTip.HeaderNext = headerNext;
          }
          HeaderTip = headerNext;
          Height = heightNext;

          double difficulty = TargetManager.GetDifficulty(
            headerNext.NBits);
          HeaderDifficulties.Add(difficulty);
          AccumulatedDifficulty += difficulty;

          headerNext = headerNext.HeaderNext;

        } while (headerNext != null);
      }
           
      void ValidateHeaderNext(
        Header headerNext, 
        int heightNext)
      {
        uint medianTimePast = GetMedianTimePast(
        headerNext.HeaderPrevious);

        if (headerNext.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              headerNext.Hash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(headerNext.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
            ErrorCode.INVALID);
        }

        int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= heightNext &&
          heightNext <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format(
              "Attempt to insert header {0} at hight {1} " +
              "prior to checkpoint hight {2}",
              headerNext.Hash.ToHexString(),
              heightNext,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Checkpoints.Find(c => c.Height == heightNext);
        if (
          checkpoint != null &&
          !checkpoint.Hash.IsEqual(headerNext.Hash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              headerNext.Hash.ToHexString(),
              heightNext,
              checkpoint.Hash.ToHexString()),
            ErrorCode.INVALID);
        }

        uint targetBits = TargetManager.GetNextTargetBits(
            headerNext.HeaderPrevious,
            (uint)heightNext);

        if (headerNext.NBits != targetBits)
        {
          throw new ChainException(
            string.Format(
              "In header {0} nBits {1} not equal to target nBits {2}",
              headerNext.Hash.ToHexString(),
              headerNext.NBits,
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