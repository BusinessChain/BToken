using System;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Chaining
{
  class MessageStreamer
  {
    const int CommandSize = 12;
    const int LengthSize = 4;
    const int ChecksumSize = 4;


    Stream Stream;

    string Command;
    uint PayloadLength;
    byte[] Payload;


    const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
    byte[] Header = new byte[HeaderSize];

    const uint MagicValue = 0xF9BEB4D9;
    const uint MagicValueByteSize = 4;
    byte[] MagicBytes = new byte[MagicValueByteSize];

    SHA256 SHA256 = SHA256.Create();



    public MessageStreamer(Stream stream)
    {
      Stream = stream;
      InitializeMagicBytes();
    }
    void InitializeMagicBytes()
    {
      for (int i = 0; i < MagicBytes.Length; i++)
      {
        MagicBytes[MagicBytes.Length - i - 1] = (byte)(MagicValue >> i * 8);
      }
    }

    public async Task Write(NetworkMessage message)
    {
      Stream.Write(MagicBytes, 0, MagicBytes.Length);

      byte[] command = Encoding.ASCII.GetBytes(
        message.Command.PadRight(CommandSize, '\0'));

      Stream.Write(command, 0, command.Length);

      byte[] payloadLength = BitConverter.GetBytes(message.Payload.Length);
      Stream.Write(payloadLength, 0, payloadLength.Length);

      byte[] checksum = CreateChecksum(message.Payload);
      Stream.Write(checksum, 0, checksum.Length);

      await Stream.WriteAsync(
        message.Payload,
        0,
        message.Payload.Length)
        .ConfigureAwait(false);
    }

    byte[] CreateChecksum(byte[] payload)
    {
      byte[] hash = SHA256.ComputeHash(SHA256.ComputeHash(payload));
      return hash.Take(ChecksumSize).ToArray();
    }

    public async Task<NetworkMessage> ReadAsync(CancellationToken cancellationToken)
    {
      await SyncStreamToMagicAsync(cancellationToken).ConfigureAwait(false);

      await ReadBytesAsync(Header, cancellationToken).ConfigureAwait(false);

      byte[] commandBytes = Header.Take(CommandSize).ToArray();
      Command = Encoding.ASCII.GetString(commandBytes).TrimEnd('\0');

      PayloadLength = BitConverter.ToUInt32(Header, CommandSize);

      if (PayloadLength > 0x02000000)
      {
        throw new ChainException("Message payload too big (over 32MB)");
      }

      Payload = new byte[(int)PayloadLength];
      await ReadBytesAsync(Payload, cancellationToken).ConfigureAwait(false);

      uint checksumMessage = BitConverter.ToUInt32(Header, CommandSize + LengthSize);
      uint checksumCalculated = BitConverter.ToUInt32(CreateChecksum(Payload), 0);

      if (checksumMessage != checksumCalculated)
      {
        throw new ChainException("Invalid Message checksum.");
      }

      return new NetworkMessage(Command, Payload);
    }

    async Task SyncStreamToMagicAsync(CancellationToken cancellationToken)
    {
      byte[] singleByte = new byte[1];
      for (int i = 0; i < MagicBytes.Length; i++)
      {
        byte expectedByte = MagicBytes[i];

        await ReadBytesAsync(singleByte, cancellationToken).ConfigureAwait(false);
        byte receivedByte = singleByte[0];
        if (expectedByte != receivedByte)
        {
          i = receivedByte == MagicBytes[0] ? 0 : -1;
        }
      }
    }
    async Task ReadBytesAsync(byte[] buffer, CancellationToken cancellationToken)
    {
      int bytesToRead = buffer.Length;
      int offset = 0;

      while (bytesToRead > 0)
      {
        int chunkSize = await Stream.ReadAsync(buffer, offset, bytesToRead, cancellationToken).ConfigureAwait(false);

        if (chunkSize == 0)
        {
          throw new ChainException("Stream returns 0 bytes signifying end of stream.");
        }

        offset += chunkSize;
        bytesToRead -= chunkSize;
      }
    }
  }
}
