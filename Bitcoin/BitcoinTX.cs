using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Bitcoin
{
  public class BitcoinTX
  {
    const byte WITNESS_IS_PRESENT_FLAG = 0x01;

    class TXInput
    {
      public UInt256 TXIDPreviousOutput { get; private set; }
      public UInt32 IndexPreviousOutput { get; private set; }
      public byte[] UnlockingScript { get; private set; }
      public UInt32 Sequence { get; private set; }


      public TXInput(
        UInt256 tXIDPreviousOutput,
        UInt32 indexPreviousOutput,
        byte[] unlockingScript,
        UInt32 sequence)
      {
        TXIDPreviousOutput = tXIDPreviousOutput;
        IndexPreviousOutput = indexPreviousOutput;
        UnlockingScript = unlockingScript;
        Sequence = sequence;
      }

      public static TXInput Parse(byte[] byteStream, ref int startIndex)
      {
        UInt256 tXIDPreviousOutput = new UInt256(byteStream, ref startIndex);

        UInt32 indexPreviousOutput = BitConverter.ToUInt32(byteStream, startIndex);
        startIndex += 4;

        int unlockingScriptLength = (int)VarInt.getUInt64(byteStream, ref startIndex);
        byte[] unlockingScript = new byte[unlockingScriptLength];
        Array.Copy(byteStream, startIndex, unlockingScript, 0, unlockingScriptLength);
        startIndex += unlockingScriptLength;

        UInt32 sequence = BitConverter.ToUInt32(byteStream, startIndex);
        startIndex += 4;

        return new TXInput(
          tXIDPreviousOutput,
          indexPreviousOutput,
          unlockingScript,
          sequence);
      }

      public byte[] GetBytes()
      {
        List<byte> byteStream = new List<byte>();

        byteStream.AddRange(TXIDPreviousOutput.GetBytes());
        byteStream.AddRange(BitConverter.GetBytes(IndexPreviousOutput));
        byteStream.AddRange(VarInt.getBytes(UnlockingScript.Length));
        byteStream.AddRange(UnlockingScript);
        byteStream.AddRange(BitConverter.GetBytes(Sequence));

        return byteStream.ToArray();
      }
    }
    class TXOutput
    {
      UInt64 Value;
      byte[] LockingScript;

      public TXOutput(
        UInt64 value,
        byte[] lockingScript)
      {
        Value = value;
        LockingScript = lockingScript;
      }

      public static TXOutput Parse(byte[] byteStream, ref int startIndex)
      {
        UInt64 value = BitConverter.ToUInt64(byteStream, startIndex);
        startIndex += 8;

        int lockingScriptLength = (int)VarInt.getUInt64(byteStream, ref startIndex);
        byte[] lockingScript = new byte[lockingScriptLength];
        Array.Copy(byteStream, startIndex, lockingScript, 0, lockingScriptLength);
        startIndex += lockingScriptLength;

        return new TXOutput(
          value,
          lockingScript);
      }

      public byte[] GetBytes()
      {
        List<byte> byteStream = new List<byte>();

        byteStream.AddRange(BitConverter.GetBytes(Value));
        byteStream.AddRange(VarInt.getBytes(LockingScript.Length));
        byteStream.AddRange(LockingScript);

        return byteStream.ToArray();
      }

    }
    class TXWitness
    {
      public static TXWitness Parse(byte[] byteStream, ref int startIndex)
      {
        return new TXWitness();
      }

      public byte[] GetBytes()
      {
        return new byte[0];
      }
    }

    UInt32 Version;
    List<TXInput> TXInputs;
    List<TXOutput> TXOutputs;
    List<TXWitness> TXWitnesses;
    UInt32 LockTime;



    BitcoinTX(
      UInt32 version,
      List<TXInput> tXInputs,
      List<TXOutput> tXOutputs,
      List<TXWitness> tXWitnesses,
      UInt32 lockTime)
    {
      Version = version;
      TXInputs = tXInputs;
      TXOutputs = tXOutputs;
      TXWitnesses = tXWitnesses;
      LockTime = lockTime;
    }

    public static BitcoinTX Parse(byte[] byteStream, ref int startIndex)
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

      int tXInputsCount = (int)VarInt.getUInt64(byteStream, ref startIndex);
      var tXInputs = new List<TXInput>();
      for (int i = 0; i < tXInputsCount; i++)
      {
        tXInputs.Add(TXInput.Parse(byteStream, ref startIndex));
      }

      int tXOutputsCount = (int)VarInt.getUInt64(byteStream, ref startIndex);
      var tXOutputs = new List<TXOutput>();
      for (int i = 0; i < tXOutputsCount; i++)
      {
        tXOutputs.Add(TXOutput.Parse(byteStream, ref startIndex));
      }

      var tXWitnesses = new List<TXWitness>();
      if ((witnessFlag & WITNESS_IS_PRESENT_FLAG) == WITNESS_IS_PRESENT_FLAG)
      {
        for (int i = 0; i < tXInputsCount; i++)
        {
          tXWitnesses.Add(TXWitness.Parse(byteStream, ref startIndex));
        }
      }

      UInt32 lockTime = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      return new BitcoinTX(
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
        byteStream.Add(WITNESS_IS_PRESENT_FLAG);
      }

      byteStream.AddRange(VarInt.getBytes(TXInputs.Count));
      foreach (TXInput tXInput in TXInputs)
      {
        byteStream.AddRange(tXInput.GetBytes());
      }

      byteStream.AddRange(VarInt.getBytes(TXOutputs.Count));
      foreach (TXOutput tXOutput in TXOutputs)
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
