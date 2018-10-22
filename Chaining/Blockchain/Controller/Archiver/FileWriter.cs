using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Archiver
    {
      public class FileWriter
      {
        Archiver Archiver;
        readonly uint ShardEnumerator;

        FileID FileID;
        FileStream FileStream;


        public FileWriter(Archiver archiver, uint shardIndex)
        {
          Archiver = archiver;
          ShardEnumerator = shardIndex;

          FileID = new FileID
          {
            ShardIndex = shardIndex,
            DirectoryIndex = 0,
            FileIndex = 0
          };

          FileStream = CreateFile();
        }

        FileStream CreateFile()
        {
          string directoryPath = Path.Combine(
            ArchiveRootPath,
            ShardHandle + FileID.ShardIndex,
            DirectoryHandle + FileID.DirectoryIndex);

          Directory.CreateDirectory(directoryPath);

          string filePath = Path.Combine(
            directoryPath,
            FileHandle + FileID.FileIndex);

          return new FileStream(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            BLOCK_REGISTER_BYTESIZE_MAX);
        }

        public BlockStore ArchiveBlock(NetworkBlock block)
        {
          byte[] headerBytes = block.Header.getBytes();
          byte[] txCount = VarInt.GetBytes(block.TXCount).ToArray();
          int payloadLength = headerBytes.Length + txCount.Length + block.Payload.Length;

          if (payloadLength > BLOCK_REGISTER_BYTESIZE_MAX)
          {
            throw new BlockchainException(string.Format("Network block too big. Maximum size '{0}', current size '{1}'", BLOCK_REGISTER_BYTESIZE_MAX, payloadLength));
          }

          if (!IsPayloadFitInCurrentFile(payloadLength))
          {
            CreateNewFileStream();
          }

          byte[] blockSizeVarIntBytes = VarInt.GetBytes(payloadLength).ToArray();
          FileStream.Write(blockSizeVarIntBytes, 0, blockSizeVarIntBytes.Length);
          FileStream.Write(headerBytes, 0, headerBytes.Length);
          FileStream.Write(txCount, 0, txCount.Length);
          FileStream.Write(block.Payload, 0, block.Payload.Length);

          return new BlockStore(FileID);
        }
        public BlockStore PeekPayloadID(int payloadLength)
        {
          if (IsPayloadFitInCurrentFile(payloadLength))
          {
            return new BlockStore(FileID);
          }
          else
          {
            FileID fileID = FileID;
            IncrementFileID(ref fileID);
            return new BlockStore(fileID);
          }
        }

        bool IsPayloadFitInCurrentFile(int blockByteSize) => FileStream.Length + blockByteSize <= BLOCK_REGISTER_BYTESIZE_MAX;

        void CreateNewFileStream()
        {
          FileStream.Close();

          IncrementFileID(ref FileID);

          FileStream = CreateFile();
        }

        public void Dispose()
        {
          FileStream.Dispose();
        }
      }
    }
  }
}
