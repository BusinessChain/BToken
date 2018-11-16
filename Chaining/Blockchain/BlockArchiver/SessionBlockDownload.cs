using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
      
      public BlockArchiver.FileWriter ShardWriter;
      ChainLocation HeaderLocation;
      
      BufferBlock<bool> SignalSessionCompletion = new BufferBlock<bool>();


      public SessionBlockDownload(BlockArchiver.FileWriter shardWriter, ChainLocation headerLocation)
      {
        ShardWriter = shardWriter;
        HeaderLocation = headerLocation;
      }

      public async Task StartAsync(INetworkChannel channel)
      {
        Channel = channel;

        if (!TryValidateBlockExisting())
        {
          await DownloadBlockAsync();
        }
        
        SignalSessionCompletion.Post(true);
      }

      async Task DownloadBlockAsync()
      {
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
        NetworkBlock block = await Channel.GetBlockAsync(HeaderLocation.Hash, cancellationToken);

        ValidateBlock(block);

        ShardWriter.ArchiveBlock(block);
      }

      bool TryValidateBlockExisting()
      {
        try
        {
          NetworkBlock block = ShardWriter.ReadBlock(HeaderLocation.Hash);
          if (block == null)
          {
            return false;
          }

          return TryValidate(block);

        }
        catch
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
      
      public async Task<SessionBlockDownload> AwaitSessionCompletedAsync()
      {
        while (true)
        {
          bool signalCompleted = await SignalSessionCompletion.ReceiveAsync();

          if (signalCompleted) { return this; }
        }
      }
    }
  }
}
