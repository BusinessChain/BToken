using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;



namespace BToken.Chaining
{
  public partial class UTXOTable
  {
    const int HASH_BYTE_SIZE = 32;
    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;

    const int LENGTH_BITS_UINT = 32;
    const int LENGTH_BITS_ULONG = 64;

    const int COUNT_NON_OUTPUT_BITS =
      COUNT_BATCHINDEX_BITS + 
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    uint MaskBatchIndexUInt32 = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
    ulong MaskBatchIndexULong64 = ~(ulong.MaxValue << COUNT_BATCHINDEX_BITS);
       
    UTXOIndex[] Tables;
    UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
    UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
    UTXOIndexUInt32Array TableUInt32Array = new UTXOIndexUInt32Array();
    
    StreamWriter LogFile;

    WalletUTXO Wallet;



    public UTXOTable(byte[] genesisBlockBytes)
    {
      LogFile = new StreamWriter("logUTXOTable", false);

      Tables = new UTXOIndex[]{
        TableUInt32,
        TableULong64,
        TableUInt32Array };

      Wallet = new WalletUTXO();
    }
             

    public void LoadImage(string pathImageRoot)
    {
      string pathImage = Path.Combine(
        pathImageRoot, 
        "UTXOImage");

      for (int c = 0; c < Tables.Length; c += 1)
      {
        string.Format("Load UTXO Table {0}.",
          Tables[c].GetType().Name)
          .Log(LogFile);

        Tables[c].LoadImage(pathImage);
      }

      string.Format(
        "Load UTXO Image from {0}",
        pathImage)
        .Log(LogFile);

      Wallet.LoadImage(pathImage);
    }

    public void Clear()
    {
      for (int c = 0; c < Tables.Length; c += 1)
      {
        Tables[c].Clear();
      }
    }


    public void InsertBlock(
      Block block,
      int indexArchive)
    {
      foreach (TX tX in block.TXs)
      {
        int lengthUTXOBits =
          COUNT_NON_OUTPUT_BITS +
          tX.TXOutputs.Count;

        if (LENGTH_BITS_UINT >= lengthUTXOBits)
        {
          uint uTXOIndex = 0;

          if (LENGTH_BITS_UINT > lengthUTXOBits)
          {
            uTXOIndex |= (uint.MaxValue << lengthUTXOBits);
          }

          TableUInt32.UTXO =
            uTXOIndex | ((uint)indexArchive & MaskBatchIndexUInt32);

          try
          {
            InsertUTXO(
              tX.Hash,
              tX.TXIDShort,
              TableUInt32);
          }
          catch (ArgumentException)
          {
            // BIP 30
            if (tX.Hash.ToHexString() == "D5D27987D2A3DFC724E359870C6644B40E497BDC0589A033220FE15429D88599" ||
               tX.Hash.ToHexString() == "E3BF3D07D4B0375638D5F1DB5255FE07BA2C4CB067CD81B84EE974B6585FB468")
            {
              Console.WriteLine("Implement BIP 30.");
            }
          }
        }
        else if (LENGTH_BITS_ULONG >= lengthUTXOBits)
        {
          ulong uTXOIndex = 0;

          if (LENGTH_BITS_ULONG > lengthUTXOBits)
          {
            uTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
          }

          TableULong64.UTXO =
            uTXOIndex |
            ((ulong)indexArchive & MaskBatchIndexULong64);

          InsertUTXO(
            tX.Hash,
            tX.TXIDShort,
            TableULong64);
        }
        else
        {
          uint[] uTXOIndex = new uint[(lengthUTXOBits + 31) / 32];

          int countUTXORemainderBits = lengthUTXOBits % 32;
          if (countUTXORemainderBits > 0)
          {
            uTXOIndex[uTXOIndex.Length - 1] |= (uint.MaxValue << countUTXORemainderBits);
          }

          TableUInt32Array.UTXO = uTXOIndex;
          TableUInt32Array.UTXO[0] |=
            (uint)indexArchive & MaskBatchIndexUInt32;

          InsertUTXO(
            tX.Hash,
            tX.TXIDShort,
            TableUInt32Array);
        }

        Wallet.DetectTXOutputsSpendable(tX);
      }

      foreach (TX tX in block.TXs)
      {
        foreach (TXInput tXInput in tX.TXInputs)
        {
          bool checkSig = Wallet.TrySpend(tXInput);

          foreach (UTXOIndex tablePrimary in Tables)
          {
            if (tablePrimary.TryGetValueInPrimaryTable(
              tXInput.TXIDOutputShort))
            {
              UTXOIndex tableCollision = null;

              for (int cc = 0; cc < Tables.Length; cc += 1)
              {
                if (tablePrimary.HasCollision(cc))
                {
                  tableCollision = Tables[cc];

                  if (tableCollision
                    .TrySpendCollision(tXInput, tablePrimary))
                  {
                    goto LABEL_LoopNextInput;
                  }
                }
              }

              tablePrimary.SpendPrimaryUTXO(
                tXInput,
                out bool allOutputsSpent);

              if (allOutputsSpent)
              {
                tablePrimary.RemovePrimary();

                if (tableCollision != null)
                {
                  tableCollision.ResolveCollision(tablePrimary);
                }
              }

              goto LABEL_LoopNextInput;
            }
          }

          throw new ProtocolException(
            string.Format(
              "Referenced TX {0} not found in UTXO table.",
              tXInput.TXIDOutputShort));

        LABEL_LoopNextInput:
          ;
        }
      }
    }

    void InsertUTXO(
      byte[] uTXOKey,
      int primaryKey,
      UTXOIndex table)
    {
      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(
            primaryKey,
            table.Address);

          table.AddUTXOAsCollision(uTXOKey);

          return;
        }
      }

      table.AddUTXOAsPrimary(primaryKey);
    }
          
    public void CreateImage(string path)
    {
      string pathUTXOImage = Path.Combine(path, "UTXOImage");
      DirectoryInfo directoryUTXOImage = 
        new DirectoryInfo(pathUTXOImage);

      Parallel.ForEach(Tables, t =>
      {
        t.BackupImage(directoryUTXOImage.FullName);
      });

      Wallet.CreateImage(directoryUTXOImage.FullName);
    }

    public string GetMetricsCSV()
    {
      return
        Tables[0].GetMetricsCSV() + "," +
        Tables[1].GetMetricsCSV() + "," +
        Tables[2].GetMetricsCSV();
    }
  }
}
