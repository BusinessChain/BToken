using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class SessionBlockDownload : INetworkSession
    {
      INetworkChannel Channel;
      
      public BlockArchiver Archiver;
      ChainLocation HeaderLocation;

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(BlockArchiver archiver, ChainLocation headerLocation)
      {
        Archiver = archiver;
        HeaderLocation = headerLocation;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        if (!await TryValidateBlockExistingAsync())
        {
          await DownloadBlockAsync();

          Debug.WriteLine("downloaded block height: '{0}'", HeaderLocation.Height);
        }
      }
      
      async Task<bool> TryValidateBlockExistingAsync()
      {
        try
        {
          NetworkBlock block = await BlockArchiver.ReadBlockAsync(HeaderLocation.Hash);
          if (block == null)
          {
            return false;
          }

          return TryValidate(block);

        }
        catch(IOException)
        {
          return false;
        }

      }
      
      bool TryValidate(NetworkBlock block)
      {
        try
        {
          ValidateBlock(block);
          return true;
        }
        catch (ChainException)
        {
          return false;
        }
      }
      void ValidateBlock(NetworkBlock block)
      {
        const int PAYLOAD_LENGTH_MAX = 0x400000;
        if (block.Payload.Length > PAYLOAD_LENGTH_MAX)
        {
          throw new ChainException(BlockCode.INVALID);
        }

        var headerHash = new UInt256(Hashing.SHA256d(block.Header.GetBytes()));
        if (!HeaderLocation.Hash.IsEqual(headerHash))
        {
          throw new ChainException(BlockCode.INVALID);
        }

        UInt256 payloadHash = PayloadParser.GetPayloadHash(block.Payload);
        if (!payloadHash.IsEqual(block.Header.MerkleRoot))
        {
          throw new ChainException(BlockCode.INVALID);
        }
      }

      async Task DownloadBlockAsync()
      {
        NetworkBlock block = await GetBlockAsync(HeaderLocation.Hash);

        ValidateBlock(block);

        try
        {
          await Archiver.ArchiveBlockAsync(block, HeaderLocation.Hash);
        }
        catch(Exception ex)
        {
          Debug.WriteLine(ex.Message);
        }
      }
      public async Task<NetworkBlock> GetBlockAsync(UInt256 hash)
      {
        var inventory = new Inventory(InventoryType.MSG_BLOCK, hash);
        await Channel.SendMessageAsync(new GetDataMessage(new List<Inventory>() { inventory }));

        var CancellationGetBlock = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

        while (true)
        {
          NetworkMessage networkMessage = await Channel.ReceiveMessageAsync(CancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            return new BlockMessage(networkMessage).NetworkBlock;
          }
        }
      }

    }
  }
}
