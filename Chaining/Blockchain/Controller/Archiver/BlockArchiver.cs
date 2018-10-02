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
    static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

    static string ShardHandle = "Shard";
    uint ShardEnumerator;

    const int ITEM_COUNT_PER_DIRECTORY = 0x40;
    static string DirectoryHandle = "Shelf";

    const int BLOCK_REGISTER_BYTESIZE_MAX = 0x400000;
    static string FileHandle = "BlockRegister";

    public BlockArchiver()
    { }

    public void LoadBlockchain(Blockchain blockchain, IBlockParser blockParser)
    {
      try
      {
        FileID fileID = new FileID
        {
          ShardIndex = 0,
          DirectoryIndex = 0,
          FileIndex = 0
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

          IncrementFileID(ref fileID);
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

    static FileStream OpenFile(FileID fileID)
    {
      string filePath = Path.Combine(
        RootDirectory.Name,
        ShardHandle + fileID.ShardIndex,
        DirectoryHandle + fileID.DirectoryIndex,
        FileHandle + fileID.FileIndex);

      return new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BLOCK_REGISTER_BYTESIZE_MAX
        );
    }
    
    static FileID IncrementFileID(ref FileID fileID)
    {
      if (fileID.FileIndex == ITEM_COUNT_PER_DIRECTORY - 1)
      {
        return new FileID()
        {
          DirectoryIndex = ++fileID.DirectoryIndex,
          FileIndex = 0,
        };
      }
      else
      {
        return new FileID()
        {
          DirectoryIndex = fileID.DirectoryIndex,
          FileIndex = ++fileID.FileIndex,
        };
      }
    }

    public FileWriter GetWriter()
    {
      try
      {
        Debug.WriteLine(Thread.CurrentThread.ManagedThreadId);
        return new FileWriter(this, ShardEnumerator++);
      }
      catch(Exception ex)
      {
        Debug.WriteLine("BlockArchiver::GetWriter: " + ex.Message);
        throw ex;
      }
    }
  }
}
