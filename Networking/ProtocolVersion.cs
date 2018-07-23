using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  /// <summary>
  /// Network protocol versioning.
  /// </summary>
  public enum ProtocolVersion : uint
  {
    PROTOCOL_VERSION = 70013,

    ALT_PROTOCOL_VERSION = 70000,

    /// <summary>
    /// Initial protocol version, to be increased after version/verack negotiation.
    /// </summary>
    INIT_PROTO_VERSION = 209,

    /// <summary>
    /// Disconnect from peers older than this protocol version.
    /// </summary>
    MIN_PEER_PROTO_VERSION = 70013,

    /// <summary>
    /// nTime field added to CAddress, starting with this version;
    /// if possible, avoid requesting addresses nodes older than this.
    /// </summary>
    CADDR_TIME_VERSION = 31402,

    /// <summary>
    /// Only request blocks from nodes outside this range of versions (START).
    /// </summary>
    NOBLKS_VERSION_START = 32000,

    /// <summary>
    /// Only request blocks from nodes outside this range of versions (END).
    /// </summary>
    NOBLKS_VERSION_END = 32400,

    /// <summary>
    /// BIP 0031, pong message, is enabled for all versions AFTER this one.
    /// </summary>
    BIP0031_VERSION = 60000,

    /// <summary>
    /// "mempool" command, enhanced "getdata" behavior starts with this version.
    /// </summary>
    MEMPOOL_GD_VERSION = 60002,

    /// <summary>
    /// "reject" command.
    /// </summary>
    REJECT_VERSION = 70002,

    /// <summary>
    /// ! "filter*" commands are disabled without NODE_BLOOM after and including this version.
    /// </summary>
    NO_BLOOM_VERSION = 70011,

    /// <summary>
    /// ! "sendheaders" command and announcing blocks with headers starts with this version.
    /// </summary>
    SENDHEADERS_VERSION = 70012,

    /// <summary>
    /// ! Version after which witness support potentially exists.
    /// </summary>
    WITNESS_VERSION = 70012,

    /// <summary>
    /// shord-id-based block download starts with this version.
    /// </summary>
    SHORT_IDS_BLOCKS_VERSION = 70014
  }


  public enum NetworkServices : ulong
  {
    Nothing = 0,

    /// <summary>
    /// NODE_NETWORK means that the node is capable of serving the block chain. It is currently
    /// set by all Bitcoin Core nodes, and is unset by SPV clients or other peers that just want
    /// network services but don't provide them.
    /// </summary>
    Network = (1 << 0),

    /// <summary>
    ///  NODE_GETUTXO means the node is capable of responding to the getutxo protocol request.
    /// Bitcoin Core does not support this but a patch set called Bitcoin XT does.
    /// See BIP 64 for details on how this is implemented.
    /// </summary>
    GetUTXO = (1 << 1),

    /// <summary> NODE_BLOOM means the node is capable and willing to handle bloom-filtered connections.
    /// Bitcoin Core nodes used to support this by default, without advertising this bit,
    /// but no longer do as of protocol version 70011 (= NO_BLOOM_VERSION)
    /// </summary>
    NODE_BLOOM = (1 << 2),

    /// <summary> Indicates that a node can be asked for blocks and transactions including
    /// witness data.
    /// </summary>
    NODE_WITNESS = (1 << 3),
  }
}
