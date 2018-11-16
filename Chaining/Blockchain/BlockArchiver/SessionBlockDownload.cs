using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;
using BToken.Chaining;

namespace BToken.Chaining
{
  public partial class Blockchain : IBlockchain
  {
    class SessionBlockDownload : INetworkSession
    {
      INetworkChannel Channel;
      
      public BlockArchiver Archiver;
      ChainLocation HeaderLocation;
      
      BufferBlock<bool> SignalSessionCompletion = new BufferBlock<bool>();


      public SessionBlockDownload(BlockArchiver archiver, ChainLocation headerLocation)
      {
        Archiver = archiver;
        HeaderLocation = headerLocation;
      }

      public async Task StartAsync(INetworkChannel channel)
      {
        Channel = channel;

        if (!await TryValidateBlockExistingAsync())
        {
          await DownloadBlockAsync();
        }
        
        SignalSessionCompletion.Post(true);
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

        if (!HeaderLocation.Hash.IsEqual(block.GetHeaderHash()))
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
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        NetworkBlock block = await Channel.GetBlockAsync(HeaderLocation.Hash, cancellationToken);

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

      public async Task AwaitSessionCompletedAsync()
      {
        while (true)
        {
          bool signalCompleted = await SignalSessionCompletion.ReceiveAsync().ConfigureAwait(false);

          if (signalCompleted) { return; }
        }
      }
    }
  }
}
