using System.Diagnostics;

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
    class BlockchainLoader
    {
      public void Load(Blockchain blockchain, IBlockParser blockParser)
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
    }
  }
}
