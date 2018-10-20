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
  public partial class Blockchain
  {
    partial class Archiver
    {
      Blockchain Blockchain;

      readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      static string ShardHandle = "Shard";
      uint ShardEnumerator;

      const int ITEM_COUNT_PER_DIRECTORY = 0x4;
      static string DirectoryHandle = "Shelf";

      const int BLOCK_REGISTER_BYTESIZE_MAX = 0x40000;
      static string FileHandle = "BlockRegister";

           
      public Archiver(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }


      public void LoadBlockchain()
      {
        using (ArchiveLoader loader = new ArchiveLoader(Blockchain))
        {
          loader.Load();
        }
        

        //try
        //{
        //  FileID fileID = new FileID
        //  {
        //    ShardIndex = 0,
        //    DirectoryIndex = 0,
        //    FileIndex = 0
        //  };

        //  while (true) // run until exception is thrown
        //  {
        //    using (FileStream blockRegisterStream = OpenFile(fileID))
        //    {
        //      int prefixInt = blockRegisterStream.ReadByte();
        //      do
        //      {
        //        NetworkBlock networkBlock = ParseNetworkBlock(blockRegisterStream, prefixInt);
        //        UInt256 headerHash = new UInt256(Hashing.SHA256d(networkBlock.Header.getBytes()));

        //        blockchain.InsertBlock(networkBlock, headerHash, new BlockStore(fileID));

        //        prefixInt = blockRegisterStream.ReadByte();
        //      } while (prefixInt > 0);
        //    }

        //    IncrementFileID(ref fileID);
        //  }
        //}
        //catch (Exception ex)
        //{
        //  Debug.WriteLine("BlockArchiver::LoadBlockchain:" + ex.Message);
        //}

      }
      static NetworkBlock ParseNetworkBlock(FileStream blockRegisterStream)
      {
        int prefixInt = blockRegisterStream.ReadByte();

        if(prefixInt == -1)
        {
          return null;
        }

        int blockLength = (int)VarInt.ParseVarInt((ulong)prefixInt, blockRegisterStream);
        byte[] blockBytes = new byte[blockLength];
        int i = blockRegisterStream.Read(blockBytes, 0, blockLength);

        return NetworkBlock.ParseBlock(blockBytes);
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

      static void IncrementFileID(ref FileID fileID)
      {
        if (fileID.FileIndex == ITEM_COUNT_PER_DIRECTORY - 1)
        {
          fileID.DirectoryIndex++;
          fileID.FileIndex = 0;
        }
        else
        {
          fileID.FileIndex++;
        }
      }

      public FileWriter GetWriter()
      {
        try
        {
          return new FileWriter(this, ShardEnumerator++);
        }
        catch (Exception ex)
        {
          Debug.WriteLine("BlockArchiver::GetWriter: " + ex.Message);
          throw ex;
        }
      }
    }
  }
}
