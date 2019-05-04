using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

using BToken.Hashing;
using BToken.Networking;

namespace BToken.Accounting
{
  class TX
  {
    public byte[] Hash;
    public TXInput[] Inputs2;
    public TXOutput[] Outputs2;
    public TXWitness[] Witnesses2;
    public int Length;

    public UInt32 Version { get; private set; }
    public List<TXInput> Inputs { get; private set; }
    public List<TXOutput> Outputs { get; private set; }
    public List<TXWitness> TXWitnesses { get; private set; }
    public UInt32 LockTime { get; private set; }

    const byte FLAG_WITNESS_IS_PRESENT = 0x01;


    TX(
      byte[] hash,
      TXInput[] inputs2,
      TXOutput[] outputs2,
      TXWitness[] witnesses2)
    {
      Hash = hash;
      Inputs2 = inputs2;
      Outputs2 = outputs2;
      Witnesses2 = witnesses2;
    }

    TX(
      UInt32 version,
      List<TXInput> tXInputs,
      List<TXOutput> tXOutputs,
      List<TXWitness> tXWitnesses,
      UInt32 lockTime)
    {
      Version = version;
      Inputs = tXInputs;
      Outputs = tXOutputs;
      TXWitnesses = tXWitnesses;
      LockTime = lockTime;
    }

    public static TX Parse2(byte[] buffer, ref int bufferIndex, SHA256 sHA256Generator)
    {
      int indexTXStart = bufferIndex;

      bufferIndex += 4; // version
      
      bool isWitnessFlagPresent = buffer[bufferIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        bufferIndex += 2;
      }

      int tXInputsCount = (int)VarInt.GetUInt64(buffer, ref bufferIndex);
      var inputs2 = new TXInput[tXInputsCount];
      for (int i = 0; i < tXInputsCount; i += 1)
      {
        inputs2[i] = TXInput.Parse(buffer, ref bufferIndex);
      }

      int tXOutputsCount = (int)VarInt.GetUInt64(buffer, ref bufferIndex);
      var outputs2 = new TXOutput[tXOutputsCount];
      for (int i = 0; i < tXOutputsCount; i += 1)
      {
        outputs2[i] = TXOutput.Parse(buffer, ref bufferIndex);
      }

      var witnesses2 = new TXWitness[tXInputsCount];
      for (int i = 0; i < tXInputsCount; i += 1)
      {
        witnesses2[i] = TXWitness.Parse(buffer, ref bufferIndex);
      }

      bufferIndex += 4; // Lock time
      
      byte[] hash = sHA256Generator.ComputeHash(
       sHA256Generator.ComputeHash(
         buffer,
         indexTXStart,
         bufferIndex - indexTXStart));
      
      return new TX(
        hash,
        inputs2,
        outputs2,
        witnesses2); ;
    }
    public static TX Parse(byte[] buffer, ref int startIndex)
    {
      UInt32 version = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      byte witnessFlag = 0x00;
      bool isWitnessFlagPresent = buffer[startIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        startIndex += 1;

        witnessFlag = buffer[startIndex];
        startIndex += 1;
      }

      int tXInputsCount = (int)VarInt.GetUInt64(buffer, ref startIndex);
      var tXInputs = new List<TXInput>();
      for (int i = 0; i < tXInputsCount; i++)
      {
        tXInputs.Add(TXInput.Parse(buffer, ref startIndex));
      }

      int tXOutputsCount = (int)VarInt.GetUInt64(buffer, ref startIndex);
      var tXOutputs = new List<TXOutput>();
      for (int i = 0; i < tXOutputsCount; i++)
      {
        tXOutputs.Add(TXOutput.Parse(buffer, ref startIndex));
      }

      var tXWitnesses = new List<TXWitness>();
      if ((witnessFlag & FLAG_WITNESS_IS_PRESENT) == FLAG_WITNESS_IS_PRESENT)
      {
        for (int i = 0; i < tXInputsCount; i++)
        {
          tXWitnesses.Add(TXWitness.Parse(buffer, ref startIndex));
        }
      }

      UInt32 lockTime = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      return new TX(
        version,
        tXInputs,
        tXOutputs,
        tXWitnesses,
        lockTime);
    }

    public byte[] GetBytes()
    {
      List<byte> byteStream = new List<byte>();

      byteStream.AddRange(BitConverter.GetBytes(Version));

      if (TXWitnesses.Any())
      {
        byteStream.Add(0x00);
        byteStream.Add(FLAG_WITNESS_IS_PRESENT);
      }

      byteStream.AddRange(VarInt.GetBytes(Inputs.Count));
      foreach (TXInput tXInput in Inputs)
      {
        byteStream.AddRange(tXInput.GetBytes());
      }

      byteStream.AddRange(VarInt.GetBytes(Outputs.Count));
      foreach (TXOutput tXOutput in Outputs)
      {
        byteStream.AddRange(tXOutput.GetBytes());
      }

      foreach (TXWitness tXWitness in TXWitnesses)
      {
        byteStream.AddRange(tXWitness.GetBytes());
      }

      byteStream.AddRange(BitConverter.GetBytes(LockTime));

      return byteStream.ToArray();
    }
  }
}
