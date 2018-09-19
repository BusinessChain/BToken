using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum BlockCode { ORPHAN, DUPLICATE, INVALID, EXPIRED };


  public partial class Blockchain
  {
    static string BlockStorageRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blockchain");

    BlockchainController Controller;

    ChainBlock BlockGenesis;
    CheckpointManager Checkpoints;

    ChainSocket SocketMain;
    HeaderLocator Locator;


    public Blockchain(
      Network network, 
      ChainBlock genesisBlock, 
      List<BlockLocation> checkpoints,
      IBlockParser blockParser)
    {
      Controller = new BlockchainController(network, this, blockParser);

      BlockGenesis = genesisBlock;
      Checkpoints = new CheckpointManager(checkpoints);
            
      SocketMain = new ChainSocket(
        blockchain: this,
        block: genesisBlock,
        hash: new UInt256(Hashing.SHA256d(genesisBlock.Header.getBytes())),
        accumulatedDifficultyPrevious: 0,
        height: 0);

      Locator = new HeaderLocator(this, SocketMain.HeaderProbe);
    }
    
    public async Task startAsync()
    {
      await Controller.StartAsync();
    }
    
    public List<BlockLocation> GetHeaderLocator() => Locator.BlockLocations;

    public ChainBlock GetBlock(UInt256 hash)
    {
      ChainSocket.SocketProbeHeader socketProbe = GetProbeAtBlock(hash);
           
      return socketProbe == null ? null : socketProbe.Block;
    }

    ChainSocket.SocketProbeHeader GetProbeAtBlock(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      ChainSocket.SocketProbeHeader probe = null;

      while (true)
      {
        if(socket == null)
        {
          return null;
        }

        probe = socket.GetProbeAtBlock(hash);

        if (probe != null)
        {
          return probe;
        }

        socket = socket.WeakerSocket;
      }
    }

    public void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      ValidateNetworkHeader(header, headerHash, out ChainSocket.SocketProbeHeader socketProbeAtHeaderPrevious);

      ChainSocket socket = socketProbeAtHeaderPrevious.InsertHeader(header, headerHash);

      if (socket == SocketMain)
      {
        Locator.Update(socket.HeightBlockTip, socket.HashBlockTip);
        return;
      }

      InsertSocket(socket);
    }
    void ValidateNetworkHeader(NetworkHeader header, UInt256 headerHash, out ChainSocket.SocketProbeHeader socketProbe)
    {
      if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }

      if (IsTimestampExpired(header.UnixTimeSeconds))
      {
        throw new BlockchainException(BlockCode.EXPIRED);
      }

      socketProbe = GetProbeAtBlock(header.HashPrevious);

      if (socketProbe == null)
      {
        throw new BlockchainException(BlockCode.ORPHAN);
      }
    }
    
    bool IsTimestampExpired(ulong unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

    public uint GetHeight() => SocketMain.HeightBlockTip;

    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth == 0 || block.BlockPrevious == null)
      {
        return block;
      }

      return GetBlockPrevious(block.BlockPrevious, --depth);
    }
    
    void InsertSocket(ChainSocket newSocket)
    {
      if (newSocket.IsStrongerThan(SocketMain))
      {
        newSocket.ConnectWeakerSocket(SocketMain);
        SocketMain = newSocket;

        Locator.Create(SocketMain.HeaderProbe);
        return;
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.IsStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.ConnectWeakerSocket(newSocket);
    }

    public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
    {
      var locatorBatchBlocksUnassignedPayload = new List<ChainBlock>();
      ChainSocket socket = SocketMain;

      do
      {
        locatorBatchBlocksUnassignedPayload.AddRange(socket.GetBlocksUnassignedPayload(batchSize));
        batchSize -= locatorBatchBlocksUnassignedPayload.Count;
        socket = socket.WeakerSocket;
      } while (batchSize > 0 && socket != null);

      return locatorBatchBlocksUnassignedPayload;
    }
  }
}
