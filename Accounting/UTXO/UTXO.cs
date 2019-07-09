using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    UTXOBuilder Builder;

    protected static string RootPath = "UTXO";

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_HEADER_BITS = 4;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 3;

    UTXOTable[] Tables = new UTXOTable[]{
        new UTXOTableUInt32(),
        new UTXOTableULong64(),
        new UTXOTableUInt32Array()};

    static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_HEADER_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;
        

    public UTXO(
      BitcoinGenesisBlock genesisBlock, 
      Headerchain headerchain, 
      Network network)
    {
      Headerchain = headerchain;
      Network = network;

      Builder = new UTXOBuilder(this, genesisBlock);
    }

    public async Task StartAsync()
    {
      await Builder.RunAsync();
    }

    void InsertUTXOs(UTXOParserData uTXOParserData)
    {
      for (int c = 0; c < Tables.Length; c += 1)
      {
      LoopUTXOItems:
        while (uTXOParserData.TryPopUTXOItem(c, out UTXOItem uTXOItem))
        {
          for (int cc = 0; cc < Tables.Length; cc += 1)
          {
            if (Tables[cc].PrimaryTableContainsKey(uTXOItem.PrimaryKey))
            {
              Tables[cc].IncrementCollisionBits(uTXOItem.PrimaryKey, c);

              Tables[c].SecondaryTableAddUTXO(uTXOItem);
              goto LoopUTXOItems;
            }
          }
          Tables[c].PrimaryTableAddUTXO(uTXOItem);
        }
      }
    }
    void SpendUTXOs(UTXOParserData uTXOParserData)
    {
      for (int t = 0; t < uTXOParserData.InputsPerTX.Length; t += 1)
      {
        int i = 0;
      LoopSpendUTXOs:
        while (i < uTXOParserData.InputsPerTX[t].Length)
        {
          TXInput input = uTXOParserData.InputsPerTX[t][i];
          
          for (int c = 0; c < Tables.Length; c += 1)
          {
            UTXOTable tablePrimary = Tables[c];

            if (tablePrimary.TryGetValueInPrimaryTable(input.PrimaryKeyTXIDOutput))
            {
              UTXOTable tableCollision = null;
              for (int cc = 0; cc < Tables.Length; cc += 1)
              {
                if (tablePrimary.HasCollision(cc))
                {
                  tableCollision = Tables[cc];

                  if (tableCollision.TrySpendCollision(input, tablePrimary))
                  {
                    i += 1;
                    goto LoopSpendUTXOs;
                  }
                }
              }

              tablePrimary.SpendPrimaryUTXO(input, out bool allOutputsSpent);

              if (allOutputsSpent)
              {
                tablePrimary.RemovePrimary();

                if (tableCollision != null)
                {
                  tableCollision.ResolveCollision(tablePrimary);
                }
              }

              i += 1;
              goto LoopSpendUTXOs;
            }
          }

          throw new UTXOException(string.Format(
            "Referenced TX {0} not found in UTXO table.",
            input.TXIDOutput.ToHexString()));
        }
      }
    }
    
    static async Task WriteFileAsync(string filePath, byte[] buffer)
    {
      try
      {
        string filePathTemp = filePath + "_temp";

        using (FileStream stream = new FileStream(
           filePathTemp,
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read,
           bufferSize: 4096,
           useAsync: true))
        {
          await stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }

        if (File.Exists(filePath))
        {
          File.Delete(filePath);
        }
        File.Move(filePathTemp, filePath);
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }
    
  }
}