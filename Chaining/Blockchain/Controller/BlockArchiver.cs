using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class BlockArchiver
  {
    readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
    DirectoryInfo RootDirectory;

    const int ITEM_COUNT_PER_DIRECTORY = 0x4;
    string DirectoryHandle = "Shelf";
    DirectoryInfo DirectoryActive;
    uint DirectoryEnumerator;

    const int BLOCK_REGISTER_BYTESIZE_MAX = 0x400000;
    string FileHandle = "BlockRegister";
    FileStream FileStreamBlockArchive;
    uint FileEnumerator;

    public BlockArchiver()
    {
      RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      DirectoryEnumerator = GetDirectoryEnumerator();
      DirectoryActive = CreateDirectory();

      FileEnumerator = GetFileEnumerator();
      FileStreamBlockArchive = CreateFile();
    }

    uint GetDirectoryEnumerator()
    {
      uint directoryEnumerator = 0;

      DirectoryInfo[] directories = RootDirectory.GetDirectories();
      foreach (DirectoryInfo directory in directories)
      {
        uint enumerator = uint.Parse(Regex.Match(directory.Name, @"\d+$").Value);
        if (enumerator >= directoryEnumerator)
        {
          directoryEnumerator = enumerator;
        }
      }

      return directoryEnumerator;
    }
    DirectoryInfo CreateDirectory()
    {
      return Directory.CreateDirectory(Path.Combine(RootDirectory.Name, DirectoryHandle + DirectoryEnumerator));
    }

    uint GetFileEnumerator()
    {
      uint fileEnumerator = 0;

      FileInfo[] fileInfos = DirectoryActive.GetFiles(".dat");
      foreach (FileInfo fileInfo in fileInfos)
      {
        uint enumerator = uint.Parse(Regex.Match(fileInfo.Name, @"\d+$").Value);
        if (enumerator >= fileEnumerator)
        {
          fileEnumerator = enumerator;
        }
      }

      return fileEnumerator;
    }
    FileStream CreateFile()
    {
      return new FileStream(
        Path.Combine(ArchiveRootPath, DirectoryActive.Name, FileHandle + FileEnumerator),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read,
        BLOCK_REGISTER_BYTESIZE_MAX
        );
    }


    public BlockStore ArchiveBlock(NetworkBlock block)
    {
      Debug.WriteLine(Thread.CurrentThread.ManagedThreadId);
      byte[] headerBytes = block.Header.getBytes();
      int blockByteSize = headerBytes.Length + block.Payload.Length;

      if (blockByteSize > BLOCK_REGISTER_BYTESIZE_MAX)
      {
        throw new BlockchainException(string.Format("Network block too big. Maximum size '{0}', current size '{1}'", BLOCK_REGISTER_BYTESIZE_MAX, blockByteSize));
      }

      if (FileStreamBlockArchive.Length + blockByteSize > BLOCK_REGISTER_BYTESIZE_MAX)
      {
        CreateNewBlockArchive();
      }

      byte[] blockSizeVarIntBytes = VarInt.GetBytes(blockByteSize).ToArray();
      FileStreamBlockArchive.Write(blockSizeVarIntBytes, 0, blockSizeVarIntBytes.Length);
      FileStreamBlockArchive.Write(headerBytes, 0, headerBytes.Length);
      FileStreamBlockArchive.Write(block.Payload, 0, block.Payload.Length);

      return new BlockStore()
      {
        FileID = new FileID()
        {
          DirectoryEnumerator = DirectoryEnumerator,
          FileEnumerator = FileEnumerator
        }
      };
    }

    void CreateNewBlockArchive()
    {
      FileStreamBlockArchive.Close();

      if (FileEnumerator >= ITEM_COUNT_PER_DIRECTORY - 1)
      {
        FileEnumerator = 0;

        DirectoryEnumerator++;
        DirectoryActive = CreateDirectory();
      }
      else
      {
        FileEnumerator++;
      }

      FileStreamBlockArchive = CreateFile();
    }

    public void LoadBlockchain(Blockchain blockchain, IBlockParser blockParser)
    {
      try
      {
        FileID fileID = new FileID
        {
          DirectoryEnumerator = 0,
          FileEnumerator = 0
        };

        while (true) // run until exception is thrown
        {
          using (FileStream blockRegisterStream = OpenFile(fileID))
          {
            int prefixInt = blockRegisterStream.ReadByte();
            while (prefixInt > 0)
            {
              int blockLength = (int)VarInt.ParseVarInt((ulong)prefixInt, blockRegisterStream);
              byte[] blockBytes = new byte[blockLength];
              int i = blockRegisterStream.Read(blockBytes, 0, blockLength);

              NetworkBlock networkBlock = NetworkBlock.ParseBlock(blockBytes);
              ChainBlock chainBlock = new ChainBlock(networkBlock.Header);
              UInt256 headerHash = new UInt256(Hashing.SHA256d(networkBlock.Header.getBytes()));

              blockchain.InsertBlock(chainBlock, headerHash);

              Validate(chainBlock, networkBlock, blockParser);

              chainBlock.BlockStore = new BlockStore() { FileID = fileID };

              prefixInt = blockRegisterStream.ReadByte();
            }
          }

          fileID = IncrementFileID(fileID);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine("BlockArchiver::LoadBlockchain:" + ex.Message);
      }

    }
    void Validate(ChainBlock chainBlock, NetworkBlock networkBlock, IBlockParser blockParser)
    {
      IBlockPayload payload = blockParser.Parse(networkBlock.Payload);
      UInt256 payloadHash = payload.GetPayloadHash();
      if (!payloadHash.IsEqual(chainBlock.Header.PayloadHash))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }
    }

    FileStream OpenFile(FileID fileID)
    {
      string filePath = Path.Combine(
        RootDirectory.Name,
        DirectoryHandle + fileID.DirectoryEnumerator,
        FileHandle + fileID.FileEnumerator);

      return new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BLOCK_REGISTER_BYTESIZE_MAX
        );
    }


    FileID IncrementFileID(FileID fileID)
    {
      if (fileID.FileEnumerator == ITEM_COUNT_PER_DIRECTORY - 1)
      {
        return new FileID()
        {
          DirectoryEnumerator = ++fileID.DirectoryEnumerator,
          FileEnumerator = 0,
        };
      }
      else
      {
        return new FileID()
        {
          DirectoryEnumerator = fileID.DirectoryEnumerator,
          FileEnumerator = ++fileID.FileEnumerator,
        };
      }
    }
  }
}
