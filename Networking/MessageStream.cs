using System;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Chaining
{
  class MessageStream
  {
    Stream Stream;

    const int CommandSize = 12;
    const int LengthSize = 4;
    const int ChecksumSize = 4;
       
    string Command;

    const int SIZE_MESSAGE_PAYLOAD_BUFFER = 0x2000000;
    byte[] Payload = new byte[SIZE_MESSAGE_PAYLOAD_BUFFER];
    int PayloadLength;

    const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
    byte[] Header = new byte[HeaderSize];
    byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9};

    SHA256 SHA256 = SHA256.Create();



    public MessageStream(Stream stream)
    {
      Stream = stream;
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



    public async Task<NetworkMessage> Read(CancellationToken cancellationToken)
    {
      await SyncStreamToMagic(cancellationToken).ConfigureAwait(false);

      await ReadBytes(Header, cancellationToken).ConfigureAwait(false);

      byte[] commandBytes = Header.Take(CommandSize).ToArray();
      Command = Encoding.ASCII.GetString(commandBytes).TrimEnd('\0');
      
      PayloadLength = BitConverter.ToInt32(Header, CommandSize);

      if (PayloadLength > SIZE_MESSAGE_PAYLOAD_BUFFER)
      {
        throw new ChainException("Message payload too big (over 32MB)");
      }

      await ReadBytes(PayloadLength, cancellationToken).ConfigureAwait(false);

      uint checksumMessage = BitConverter.ToUInt32(Header, CommandSize + LengthSize);
      uint checksumCalculated = BitConverter.ToUInt32(CreateChecksum(Payload), 0);

      if (checksumMessage != checksumCalculated)
      {
        throw new ChainException("Invalid Message checksum.");
      }

      return new NetworkMessage(Command, Payload);
    }



    async Task SyncStreamToMagic(CancellationToken cancellationToken)
    {
      byte[] singleByte = new byte[1];
      for (int i = 0; i < MagicBytes.Length; i++)
      {
        byte expectedByte = MagicBytes[i];

        await ReadBytes(singleByte, cancellationToken).ConfigureAwait(false);
        byte receivedByte = singleByte[0];
        if (expectedByte != receivedByte)
        {
          i = receivedByte == MagicBytes[0] ? 0 : -1;
        }
      }
    }

    byte[] CreateChecksum(byte[] payload)
    {
      byte[] hash = SHA256.ComputeHash(
        SHA256.ComputeHash(payload));

      return hash.Take(ChecksumSize).ToArray();
    }

    async Task ReadBytes(
      int bytesToRead, 
      CancellationToken cancellationToken)
    {
      int offset = 0;

      while (bytesToRead > 0)
      {
        int chunkSize = await Stream.ReadAsync(
          Payload, 
          offset, 
          bytesToRead, 
          cancellationToken).ConfigureAwait(false);

        if (chunkSize == 0)
        {
          throw new ChainException(
            "Stream returns 0 bytes signifying end of stream.");
        }

        offset += chunkSize;
        bytesToRead -= chunkSize;
      }
    }
  }
}
