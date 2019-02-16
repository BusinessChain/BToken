using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Hashing;
using BToken.Networking;

namespace BToken.Accounting
{
  class TX
  {
    const byte FLAG_WITNESS_IS_PRESENT = 0x01;

    public UInt32 Version { get; private set; }
    public List<TXInput> Inputs { get; private set; }
    public List<TXOutput> Outputs { get; private set; }
    public List<TXWitness> TXWitnesses { get; private set; }
    public UInt32 LockTime { get; private set; }



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

    public static TX Parse(byte[] byteStream, ref int startIndex)
    {
      UInt32 version = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      byte witnessFlag = 0x00;
      bool isWitnessFlagPresent = byteStream[startIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        startIndex += 1;

        witnessFlag = byteStream[startIndex];
        startIndex += 1;
      }

      int tXInputsCount = (int)VarInt.GetUInt64(byteStream, ref startIndex);
      var tXInputs = new List<TXInput>();
      for (int i = 0; i < tXInputsCount; i++)
      {
        tXInputs.Add(TXInput.Parse(byteStream, ref startIndex));
      }

      int tXOutputsCount = (int)VarInt.GetUInt64(byteStream, ref startIndex);
      var tXOutputs = new List<TXOutput>();
      for (int i = 0; i < tXOutputsCount; i++)
      {
        tXOutputs.Add(TXOutput.Parse(byteStream, ref startIndex));
      }

      var tXWitnesses = new List<TXWitness>();
      if ((witnessFlag & FLAG_WITNESS_IS_PRESENT) == FLAG_WITNESS_IS_PRESENT)
      {
        for (int i = 0; i < tXInputsCount; i++)
        {
          tXWitnesses.Add(TXWitness.Parse(byteStream, ref startIndex));
        }
      }

      UInt32 lockTime = BitConverter.ToUInt32(byteStream, startIndex);
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

  static class BitcoinTXExtensionMethods
  {
    public static UInt256 GetTXHash(this TX bitcoinTX)
    {
      return new UInt256(SHA256d.Compute(bitcoinTX.GetBytes()));
    }
  }
}
