using System;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace BToken.Networking
{
  partial class Network
  {

    /// <summary>
    /// Reads and writes raw Bitcoin Messages from the provided stream.
    /// </summary>
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
        Stream.Write(MagicBytes, 0, MagicBytes.Length);

        byte[] command = Encoding.ASCII.GetBytes(networkMessage.Command.PadRight(CommandSize, '\0'));
        Stream.Write(command, 0, CommandSize);

        byte[] payloadLength = BitConverter.GetBytes(networkMessage.Payload.Length);
        Stream.Write(payloadLength, 0, LengthSize);
        
        byte[] checksum = createChecksum(networkMessage.Payload);
        Stream.Write(checksum, 0, ChecksumSize);

        await Stream.WriteAsync(networkMessage.Payload, 0, networkMessage.Payload.Length);
      }

      byte[] createChecksum(byte[] payload)
      {
        return Hashing.sha256d(payload).Take(ChecksumSize).ToArray();
      }

      public async Task<NetworkMessage> ReadAsync()
      {
        await syncStreamToMagicAsync().ConfigureAwait(false); //synchron

        await readBytesAsync(Header).ConfigureAwait(false);//synchron
        getCommand();
        getPayloadLength();

        await parseMessagePayload().ConfigureAwait(false);
        verifyChecksum();

        return new NetworkMessage(Command, Payload);
      }
      async Task syncStreamToMagicAsync()
      {
        byte[] singleByte = new byte[1];
        for (int i = 0; i < MagicBytes.Length; i++)
        {
          byte expectedByte = MagicBytes[i];

          await readBytesAsync(singleByte).ConfigureAwait(false);
          byte receivedByte = singleByte[0];
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
          throw new NetworkException("Message payload too big (over 32MB)");
        }
      }
      async Task parseMessagePayload()
      {
        Payload = new byte[(int)PayloadLength];
        await readBytesAsync(Payload).ConfigureAwait(false);
      }
      void verifyChecksum()
      {
        uint checksumMessage = BitConverter.ToUInt32(Header, CommandSize + LengthSize);
        uint checksumCalculated = BitConverter.ToUInt32(createChecksum(Payload), 0);

        if (checksumMessage != checksumCalculated)
        {
          throw new NetworkException("Invalid Message checksum.");
        }
      }
      async Task readBytesAsync(byte[] buffer)
      {
        int bytesToRead = buffer.Length;
        int offset = 0;

        while (bytesToRead > 0)
        {
          int chunkSize = await Stream.ReadAsync(buffer, offset, bytesToRead).ConfigureAwait(false);

          if(chunkSize == 0)
          {
            throw new InvalidOperationException("Stream returns 0 bytes signifying end of stream. Code will be caught in a forever-loop.");
          }

          offset += chunkSize;
          bytesToRead -= chunkSize;
        }
      }
    }
  }
}
