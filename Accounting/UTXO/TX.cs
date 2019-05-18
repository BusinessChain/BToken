using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    class TX
    {
      public byte[] Hash;
      public TXInput[] Inputs;
      public TXOutput[] Outputs;
      public TXWitness[] Witnesses;

      public int PrimaryKey;
      public int LengthUTXOBits;

      const byte FLAG_WITNESS_IS_PRESENT = 0x01;


      TX(
        byte[] hash,
        TXInput[] inputs,
        TXOutput[] outputs)
      {
        Hash = hash;
        PrimaryKey = BitConverter.ToInt32(hash, 0);

        Inputs = inputs;

        Outputs = outputs;
        LengthUTXOBits = CountHeaderPlusCollisionBits + outputs.Length;
      }

      public static TX Parse(byte[] buffer, ref int bufferIndex, SHA256 sHA256Generator)
      {
        int indexTXStart = bufferIndex;

        bufferIndex += 4; // version

        bool isWitnessFlagPresent = buffer[bufferIndex] == 0x00;
        if (isWitnessFlagPresent)
        {
          bufferIndex += 2;
        }

        int tXInputsCount = VarInt.GetInt32(buffer, ref bufferIndex);
        var inputs = new TXInput[tXInputsCount];
        for (int i = 0; i < tXInputsCount; i += 1)
        {
          inputs[i] = TXInput.Parse(buffer, ref bufferIndex);
        }

        int tXOutputsCount = VarInt.GetInt32(buffer, ref bufferIndex);
        var outputs = new TXOutput[tXOutputsCount];
        for (int i = 0; i < tXOutputsCount; i += 1)
        {
          outputs[i] = TXOutput.Parse(buffer, ref bufferIndex);
        }

        if (isWitnessFlagPresent)
        {
          var witnesses = new TXWitness[tXInputsCount];
          for (int i = 0; i < tXInputsCount; i += 1)
          {
            witnesses[i] = TXWitness.Parse(buffer, ref bufferIndex);
          }
        }

        bufferIndex += 4; // Lock time

        byte[] hash = sHA256Generator.ComputeHash(
         sHA256Generator.ComputeHash(
           buffer,
           indexTXStart,
           bufferIndex - indexTXStart));

        return new TX(
          hash,
          inputs,
          outputs);
      }

    }
  }
}
