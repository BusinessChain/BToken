using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum BlockCode { ORPHAN, DUPLICATE, INVALID, EXPIRED };


  public partial class Blockchain
  {
    Network Network;
    BlockchainController Controller;
    
    ChainBlock BlockGenesis;
    CheckpointManager Checkpoints;
    IBlockPayloadParser BlockPayloadParser;

    ChainSocket SocketMain;
    HeaderLocator Locator;

    Stopwatch StopWatch = new Stopwatch();


    public Blockchain(Network network, ChainBlock genesisBlock, List<BlockLocation> checkpoints, IBlockPayloadParser blockPayloadParser)
    {
      Network = network;
      Controller = new BlockchainController(network, this);

      BlockGenesis = genesisBlock;
      Checkpoints = new CheckpointManager(checkpoints);
      BlockPayloadParser = blockPayloadParser;

      SocketMain = new ChainSocket(
        blockchain: this,
        block: genesisBlock,
        hash: CalculateHash(genesisBlock.Header.getBytes()),
        accumulatedDifficultyPrevious: 0,
        height: 0);

      Locator = new HeaderLocator(this, SocketMain.Probe);
    }
    static UInt256 CalculateHash(byte[] byteStream)
    {
      byte[] hashBytes = Hashing.sha256d(byteStream);
      return new UInt256(hashBytes);
    }

    public async Task startAsync()
    {
      await Controller.StartAsync();
    }

    public List<BlockLocation> GetHeaderLocator() => Locator.BlockLocations;

    ChainBlock GetBlock(UInt256 hash)
    {
      StopWatch.Start();

      ChainSocket.SocketProbe socketProbe = GetProbeAtBlock(hash);

      StopWatch.Stop();
      TimeSpan ts = StopWatch.Elapsed;
      string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
        ts.Hours, 
        ts.Minutes, 
        ts.Seconds, 
        ts.Milliseconds / 10);
      Debug.WriteLine("Runtime " + elapsedTime);
      StopWatch.Reset();



      return socketProbe == null ? null : socketProbe.Block;
    }

    ChainSocket.SocketProbe GetProbeAtBlock(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      ResetProbes();

      while (socket != null)
      {
        if (socket.Probe.IsHash(hash))
        {
          return socket.Probe;
        }

        if (socket.Probe.IsGenesis())
        {
          socket.Bypass();

          if (socket.IsWeakerSocketProbeStrongerThan(socket.StrongerSocketActive))
          {
            socket = socket.WeakerSocketActive;
          }
          else
          {
            socket = socket.StrongerSocket;
          }
          continue;
        }

        if (socket.IsProbeStrongerThan(socket.StrongerSocket))
        {
          if (socket.IsWeakerSocketProbeStronger())
          {
            socket = socket.WeakerSocketActive;
          }

          socket.Probe.Push();
        }
        else
        {
          socket = socket.StrongerSocket;
        }

      }

      return null;
    }
    void ResetProbes()
    {
      ChainSocket socket = SocketMain;

      while (socket != null)
      {
        socket.Reset();
        socket = socket.WeakerSocket;
      }
    }

    void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      ValidateHeader(header, headerHash, out ChainSocket.SocketProbe socketProbe);

      ChainSocket socket = socketProbe.InsertHeader(header, headerHash);

      if (socket == SocketMain)
      {
        Locator.Update(socket.Height, socket.Hash);
        return;
      }

      InsertSocket(socket);
    }
    void ValidateHeader(NetworkHeader header, UInt256 headerHash, out ChainSocket.SocketProbe socketProbe)
    {
      if (headerHash.isGreaterThan(UInt256.ParseFromCompact(header.NBits)))
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
    
    void InsertBlock(NetworkBlock networkBlock, UInt256 headerHash)
    {
      ChainBlock chainBlock = GetBlock(headerHash);

      IBlockPayload payload = BlockPayloadParser.Parse(networkBlock.Payload);
      chainBlock.InsertPayload(payload);
    }

    bool IsTimestampExpired(ulong unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

    uint GetHeight() => SocketMain.Height;

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

        Locator.Create(SocketMain.Probe);
        return;
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.IsStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.ConnectWeakerSocket(newSocket);
    }
  }
}
