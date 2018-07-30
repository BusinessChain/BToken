using System;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {

    /// <summary>
    /// Reads and writes raw Bitcoin Messages from the provided stream.
    /// </summary>
    class MessageStreamer : IDisposable
    {
      Stream Stream;

      const int CommandSize = 12;
      string Command;

      const int LengthSize = 4;
      uint PayloadLength;
      byte[] Payload;

      const int ChecksumSize = 4;
      uint Checksum;

      const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
      byte[] Header = new byte[HeaderSize];

      byte[] MagicBytes = new byte[MagicValueByteSize];



      public MessageStreamer(Stream stream)
      {
        Stream = stream;
        populateMagicBytes();
      }
      void populateMagicBytes()
      {
        for (int i = 0; i < MagicBytes.Length; i++)
        {
          MagicBytes[MagicBytes.Length - i - 1] = (byte)(MagicValue >> i * 8);
        }
      }

      public async Task WriteAsync(NetworkMessage networkMessage)
      {
        await Stream.WriteAsync(MagicBytes, 0, MagicBytes.Length).ConfigureAwait(false);

        byte[] command = Encoding.ASCII.GetBytes(networkMessage.Command.PadRight(CommandSize, '\0'));
        await Stream.WriteAsync(command, 0, CommandSize).ConfigureAwait(false);

        byte[] payloadLength = BitConverter.GetBytes(networkMessage.Payload.Length);
        await Stream.WriteAsync(payloadLength, 0, LengthSize).ConfigureAwait(false);
        
        byte[] checksum = createChecksum(networkMessage.Payload);
        await Stream.WriteAsync(checksum, 0, ChecksumSize).ConfigureAwait(false);

        await Stream.WriteAsync(networkMessage.Payload, 0, networkMessage.Payload.Length).ConfigureAwait(false);
      }

      byte[] createChecksum(byte[] payload)
      {
        return Hashing.sha256d(payload).Take(ChecksumSize).ToArray();
      }

      public async Task<NetworkMessage> ReadAsync()
      {
        await syncStreamToMagicAsync();

        await readBytesAsync(Header);
        getCommand();
        getPayloadLength();

        await parseMessagePayload();
        verifyChecksum();

        return new NetworkMessage(Command, Payload);
      }
      async Task syncStreamToMagicAsync()
      {
        byte[] bytes = new byte[1];
        for (int i = 0; i < MagicBytes.Length; i++)
        {
          byte expectedByte = MagicBytes[i];

          await readBytesAsync(bytes);

          byte receivedByte = bytes[0];
          if (expectedByte != receivedByte)
          {
            i = receivedByte == MagicBytes[0] ? 0 : -1;
          }
        }
      }
      void getCommand()
      {
        byte[] commandBytes = Header.Take(CommandSize).ToArray();
        Command = Encoding.ASCII.GetString(commandBytes).TrimEnd('\0');
      }
      void getPayloadLength()
      {
        PayloadLength = BitConverter.ToUInt32(Header, CommandSize);

        if (PayloadLength > 0x02000000)
        {
          throw new NetworkProtocolException("Message payload too big (over 32MB)");
        }
      }
      async Task parseMessagePayload()
      {
        Payload = new byte[(int)PayloadLength];
        await readBytesAsync(Payload);
      }
      void verifyChecksum()
      {
        uint checksumMessage = BitConverter.ToUInt32(Header, CommandSize + LengthSize);
        uint checksumCalculated = BitConverter.ToUInt32(createChecksum(Payload), 0);

        if (checksumMessage != checksumCalculated)
        {
          throw new NetworkProtocolException("Invalid Message checksum.");
        }
      }
      async Task readBytesAsync(byte[] buffer)
      {
        int bytesToRead = buffer.Length;
        int offset = 0;

        while (bytesToRead > 0)
        {
          int chunkSize = await Stream.ReadAsync(buffer, offset, bytesToRead);

          offset += chunkSize;
          bytesToRead -= chunkSize;
        }
      }

      public void Dispose()
      {
        Stream.Dispose();
      }
    }
  }
}
