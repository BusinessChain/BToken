using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class BlockArchiver
  {
    public class FileWriter
    {
      BlockArchiver Archiver;
      readonly uint ShardEnumerator;

      FileID FileID;
      FileStream FileStream;


      public FileWriter(BlockArchiver archiver, uint shardEnumerator)
      {
        Archiver = archiver;
        ShardEnumerator = shardEnumerator;

        FileID = new FileID();
        FileStream = CreateFile();
      }

      FileStream CreateFile()
      {
        string directoryPath = Path.Combine(
          ArchiveRootPath,
          ShardHandle + ShardEnumerator,
          DirectoryHandle + FileID.DirectoryEnumerator);

        Directory.CreateDirectory(directoryPath);

        string filePath = Path.Combine(
          directoryPath,
          FileHandle + FileID.FileEnumerator);

        return new FileStream(
          filePath,
          FileMode.OpenOrCreate,
          FileAccess.ReadWrite,
          FileShare.Read,
          BLOCK_REGISTER_BYTESIZE_MAX
          );
      }

      public BlockStore ArchiveBlock(NetworkBlock block)
      {
        byte[] headerBytes = block.Header.getBytes();
        int blockByteSize = headerBytes.Length + block.Payload.Length;

        if (blockByteSize > BLOCK_REGISTER_BYTESIZE_MAX)
        {
          throw new BlockchainException(string.Format("Network block too big. Maximum size '{0}', current size '{1}'", BLOCK_REGISTER_BYTESIZE_MAX, blockByteSize));
        }

        if (FileStream.Length + blockByteSize > BLOCK_REGISTER_BYTESIZE_MAX)
        {
          CreateNewFileStream();
        }

        byte[] blockSizeVarIntBytes = VarInt.GetBytes(blockByteSize).ToArray();
        FileStream.Write(blockSizeVarIntBytes, 0, blockSizeVarIntBytes.Length);
        FileStream.Write(headerBytes, 0, headerBytes.Length);
        FileStream.Write(block.Payload, 0, block.Payload.Length);

        return new BlockStore() { FileID = FileID };
      }

      void CreateNewFileStream()
      {
        FileStream.Close();

        FileID = IncrementFileID(FileID);
        
        FileStream = CreateFile();
      }

      public void Dispose()
      {
        FileStream.Dispose();
      }
    }
  }
}
