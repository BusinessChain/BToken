using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;



namespace BToken.Chaining
{
  partial class Headerchain
  {
    class ChainInserter
    {
      Headerchain Headerchain;

      Chain Chain;
      public Header Header;
      public int Height;
      public double AccumulatedDifficulty;



      public ChainInserter(Headerchain headerchain)
      {
        Headerchain = headerchain;

        Chain = headerchain.MainChain;
        Header = headerchain.MainChain.HeaderTip;
        Height = headerchain.MainChain.Height;
        AccumulatedDifficulty = headerchain.MainChain.AccumulatedDifficulty;
      }



      void GotoHeaderRoot(ref Header header, byte[] stopHash)
      {
        while (!Header.HeaderHash.IsEqual(header.HashPrevious))
        {
          if (Headerchain.MainChain.HeaderRoot == Header)
          {
            throw new ChainException(
              string.Format(
                "Previous header {0} \n " +
                "of header {1} not found in chain.",
                header.HashPrevious.ToHexString(),
                header.HeaderHash.ToHexString()),
              ErrorCode.ORPHAN);
          }

          AccumulatedDifficulty -= TargetManager.GetDifficulty(
            Header.NBits);

          Height--;

          Header = Header.HeaderPrevious;
        }

        while (true)
        {
          byte[] headerHash = header.HeaderHash;

          if(headerHash.IsEqual(stopHash))
          {
            throw new ChainException(string.Format(
              "stopHash {0} reached.",
              stopHash.ToHexString()));
          }

          int indexHeaderDuplicate = Header.HeadersNext.FindIndex(
            h => h.HeaderHash.IsEqual(headerHash));

          if (indexHeaderDuplicate == -1)
          {
            break;
          }

          Header = Header.HeadersNext[indexHeaderDuplicate];

          AccumulatedDifficulty += TargetManager.GetDifficulty(
            Header.NBits);

          Height++;

          if (header.HeadersNext.Any())
          {
            header = header.HeadersNext[0];
            continue;
          }

          break;
        }
      }

      public void InsertTentatively(Header header, byte[] stopHash)
      {
        if(Header == Chain.HeaderTip)
        {
          GotoHeaderRoot(ref header, stopHash);

          Headerchain.HeaderRootTentative = Header;
          Headerchain.HeightRootTentatively = Height;
        }
        else if(!Header.HeaderHash.IsEqual(header.HashPrevious))
        {
          throw new ChainException(
            string.Format(
              "HeaderPrevious {0} not equal to \n" +
              "last header inserted tentatively {1}.",
              header.HashPrevious.ToHexString(),
              Header.HeaderHash.ToHexString()),
            ErrorCode.INVALID);
        }

        header.HeaderPrevious = Header;

        List<Header> headersValidated = ValidateChain(header);

        Header.HeadersNext.Add(header);

        Header = headersValidated.Last();
        Height += headersValidated.Count;
        AccumulatedDifficulty += headersValidated
          .Sum(h => TargetManager.GetDifficulty(h.NBits));
      }



      public Chain InsertHeaderRoot(Header headerRoot)
      {
        if (!GoTo(headerRoot.HashPrevious))
        {
          throw new ChainException(
            string.Format(
              "previous header {0}\n of header {1} not found in chain",
              headerRoot.HashPrevious.ToHexString(),
              headerRoot.HeaderHash.ToHexString()),
            ErrorCode.ORPHAN);
        }

        if (Header.HeadersNext
          .Any(h => h.HeaderHash.IsEqual(headerRoot.HeaderHash)))
        {
          throw new ChainException(
            string.Format(
              "duplicate header {0} \n attempting to connect to header {1}",
              headerRoot.HeaderHash.ToHexString(),
              headerRoot.HashPrevious.ToHexString()),
            ErrorCode.DUPLICATE);
        }

        // Falls fork, meta validierung und dann falls stärker, 
        // reorg und UTXO validierung, ansonsten die ganze kalde verwerfen

        // falls keine fork, kein orphan, kein duplikat -> Main chain extension
        // NOrmales validieren und anhängen

        headerRoot.HeaderPrevious = Header;

        List<Header> headersValidated = ValidateChain(headerRoot);

        //Header.HeadersNext.Add(headerRoot);
        //headersValidated.ForEach(h => Headerchain.UpdateHeaderIndex(h));

        if (Header == Chain.HeaderTip)
        {
          Chain.HeaderTip = headersValidated.Last();
          Chain.Height += headersValidated.Count;
          Chain.AccumulatedDifficulty += headersValidated
            .Sum(h => TargetManager.GetDifficulty(h.NBits));

          if (Chain == Headerchain.MainChain)
          {
            Headerchain.Locator.Update();
            return null;
          }

          return Chain;
        }
        else
        {
          Chain chainFork = new Chain(
            headerRoot: headerRoot,
            height: Height + headersValidated.Count,
            accumulatedDifficulty: AccumulatedDifficulty + headersValidated
            .Sum(h => TargetManager.GetDifficulty(h.NBits)));

          Headerchain.SecondaryChains.Add(chainFork);

          return chainFork;
        }
      }
      
      bool GoTo(byte[] hash)
      {
        while (true)
        {
          if (Header.HeaderHash.IsEqual(hash))
          {
            return true;
          }

          if (Headerchain.MainChain.HeaderRoot == Header)
          {
            return false;
          }

          AccumulatedDifficulty -= TargetManager.GetDifficulty(
            Header.NBits);

          Height--;

          Header = Header.HeaderPrevious;
        }
      }
      
      List<Header> ValidateChain(Header headerRoot)
      {
        var headersValidated = new List<Header>();
        Header header = headerRoot;

        while (true)
        {
          uint medianTimePast = GetMedianTimePast(header.HeaderPrevious);
          if (header.UnixTimeSeconds < medianTimePast)
          {
            throw new ChainException(
              string.Format(
                "Header {0} with unix time {1} " +
                "is older than median time past {2}.",
                header.HeaderHash.ToHexString(),
                DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
                DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
              ErrorCode.INVALID);
          }

          int heightHeader =
            Height + 1 + headersValidated.Count;

          int hightHighestCheckpoint = Headerchain.Checkpoints.Max(x => x.Height);

          if (
            hightHighestCheckpoint <= Headerchain.MainChain.Height &&
            heightHeader <= hightHighestCheckpoint)
          {
            throw new ChainException(
              string.Format(
                "Attempt to insert header {0} at hight {1} " +
                "prior to checkpoint hight {2}",
                header.HeaderHash.ToHexString(),
                heightHeader,
                hightHighestCheckpoint),
              ErrorCode.INVALID);
          }

          HeaderLocation checkpoint = 
            Headerchain.Checkpoints.Find(c => c.Height == heightHeader);
          if (checkpoint != null && !checkpoint.Hash.IsEqual(header.HeaderHash))
          {
            throw new ChainException(
              string.Format(
                "Header {0} at hight {1} not equal to checkpoint hash {2}",
                header.HeaderHash.ToHexString(),
                heightHeader,
                checkpoint.Hash.ToHexString()),
              ErrorCode.INVALID);
          }

          uint targetBits = TargetManager.GetNextTargetBits(
              header.HeaderPrevious,
              (uint)heightHeader);

          if (header.NBits != targetBits)
          {
            throw new ChainException(
              string.Format(
                "In header {0} nBits {1} not equal to target nBits {2}",
                header.HeaderHash.ToHexString(),
                header.NBits,
                targetBits),
              ErrorCode.INVALID);
          }

          headersValidated.Add(header);

          if (header.HeadersNext.Any())
          {
            header = header.HeadersNext[0];
          }
          else
          {
            return headersValidated;
          }
        }
      }
      
      uint GetMedianTimePast(Header header)
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
