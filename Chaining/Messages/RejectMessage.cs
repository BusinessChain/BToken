using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class RejectMessage : NetworkMessage
  {
    const int TXAndBlockExtraDataLength = 32;
    public enum RejectCode : byte
    {
      MALFORMED = 0x01,
      INVALID = 0x10,
      OBSOLETE = 0x11,
      DUPLICATE = 0x12,
      NONSTANDARD = 0x40,
      DUST = 0x41,
      INSUFFICIENTFEE = 0x42,
      CHECKPOINT = 0x43
    }


    Byte RejectionCode;
    string MessageTypeRejected;
    public string RejectionReason { get; private set; }
    byte[] ExtraData;


    public RejectMessage(byte[] payload) : base("reject", payload)
    {
      deserializePayload();
    }
    void deserializePayload()
    {
      int startIndex = 0;

      MessageTypeRejected = VarString.GetString(Payload, ref startIndex);

      RejectionCode = Payload[startIndex];
      startIndex += 1;

      RejectionReason = VarString.GetString(Payload, ref startIndex);

      deserializeExtraData(ref startIndex);
    }
    void deserializeExtraData(ref int startIndex)
    {
      if (startIndex == Payload.Length)
      {
        return; // No more data in Payload: Peer did not attach any extra data
      }

      if (MessageTypeRejected == "tx" || MessageTypeRejected == "block")
      {
        int extraDataLength = Payload.Length - startIndex;
        if (extraDataLength != TXAndBlockExtraDataLength)
        {
          throw new ProtocolException(string.Format("Provided extra data length '{0}' not consistent with protocol '{1}'", extraDataLength, TXAndBlockExtraDataLength));
        }

        ExtraData = new byte[TXAndBlockExtraDataLength];
        Array.Copy(Payload, startIndex, ExtraData, 0, TXAndBlockExtraDataLength);
      }

    }

    public RejectMessage(string messageTypeRejected, RejectCode rejectionCode, string rejectionReason)
      : this(messageTypeRejected, rejectionCode, rejectionReason, new byte[0]) { }
    public RejectMessage(string messageTypeRejected, RejectCode rejectionCode, string rejectionReason, byte[] extraData) : base("reject")
    {
      RejectionCode = (Byte)rejectionCode;
      MessageTypeRejected = messageTypeRejected;
      RejectionReason = rejectionReason;
      ExtraData = extraData;

      createRejectPayload();
    }
    void createRejectPayload()
    {
      List<byte> rejectPayload = new List<byte>();

      rejectPayload.AddRange(VarString.GetBytes(MessageTypeRejected));
      rejectPayload.Add((byte)RejectionCode);
      rejectPayload.AddRange(VarString.GetBytes(RejectionReason));
      rejectPayload.AddRange(ExtraData);

      Payload = rejectPayload.ToArray();
    }
  }
}
