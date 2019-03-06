﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOBuilderBatch
    {
      UTXO UTXO;
      UTXOBuilder UTXOBuilder;

      public List<HeaderLocation> HeaderLocations;
      public Dictionary<byte[], int[]> InputsUnfunded;
      public Dictionary<byte[], byte[]> UTXOs;
      public int BatchIndex;

      public UTXOBuilderBatch(
        UTXO uTXO,
        UTXOBuilder uTXOBuilder,
        List<HeaderLocation> headerLocations,
        int batchIndex)
      {
        UTXO = uTXO;
        UTXOBuilder = uTXOBuilder;
        HeaderLocations = headerLocations;
        InputsUnfunded = new Dictionary<byte[], int[]>(new EqualityComparerByteArray());
        UTXOs = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
        BatchIndex = batchIndex;
      }

      public async Task BuildAsync()
      {
        try
        {
          foreach (HeaderLocation headerLocation in HeaderLocations)
          {
            Block block = await UTXO.GetBlockAsync(headerLocation.Hash);

            UTXOTransaction.BuildBlock(block, InputsUnfunded, UTXOs);
          }

          await UTXOBuilder.MergeBatchAsync(this);
        }
        catch (UTXOException ex)
        {
          Console.WriteLine("Build batch '{0}' threw UTXOException: '{1}'",
            BatchIndex, ex.Message);
        }
        catch (Exception ex)
        {
          Console.WriteLine("Build batch '{0}' threw unexpected exception: '{1}'",
            BatchIndex, ex.Message);
        }
      }

    }
  }
}
