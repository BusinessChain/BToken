using System;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    partial class Peer
    {
      class PeerConnectionManager
      {
        Peer Peer;

        bool VerAckReceived;
        VersionMessage VersionMessageRemote;

        uint? ChainheightCurrent; // wo und wann wird dies gesetzt?
        TimeSpan OffsetToLocalUTC;
        int PenaltyScore;

        public PeerConnectionManager(Peer peer)
        {
          Peer = peer;
        }

        public async Task receiveResponseToVersionMessageAsync(NetworkMessage messageRemote)
        {
          switch (messageRemote.Command)
          {
            case "verack":
              VerAckReceived = true;
              break;

            case "version":
              VersionMessageRemote = new VersionMessage(messageRemote.Payload);
              await Peer.SendMessageAsync(responseToVersionMessageRemote()).ConfigureAwait(false);
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(messageRemote.Payload);
              throw new ChainException(string.Format("Peer rejected handshake: '{0}'", rejectMessage.RejectionReason));

            default:
              throw new ChainException(string.Format("Handshake aborted: Received improper message '{0}' during handshake session.", messageRemote.Command));
          }
        }
        NetworkMessage responseToVersionMessageRemote()
        {
          string rejectionReason = "";

          if (VersionMessageRemote.ProtocolVersion < ProtocolVersion)
          {
            rejectionReason = string.Format("Outdated version '{0}', minimum expected version is '{1}'.", VersionMessageRemote.ProtocolVersion, ProtocolVersion);
          }

          if (((ServiceFlags)VersionMessageRemote.NetworkServicesLocal).HasFlag(NetworkServicesRemoteRequired))
          {
            rejectionReason = string.Format("Network services '{0}' do not meet requirement '{1}'.", VersionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
          }

          if (VersionMessageRemote.UnixTimeSeconds - getUnixTimeSeconds() > 2 * 60 * 60)
          {
            rejectionReason = string.Format("Unix time '{0}' more than 2 hours in the future compared to local time '{1}'.", VersionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
          }

          if (VersionMessageRemote.Nonce == Nonce)
          {
            rejectionReason = string.Format("Duplicate Nonce '{0}'.", Nonce);
          }

          if ((RelayOptionFlags)VersionMessageRemote.RelayOption != RelayOptionFlags.SendTxStandard)
          {
            rejectionReason = string.Format("We only support RelayOption = '{0}'.", RelayOptionFlags.SendTxStandard);
          }

          if (rejectionReason != "")
          {
            return new RejectMessage(VersionMessageRemote.Command, RejectMessage.RejectCode.OBSOLETE, rejectionReason);
          }
          else
          {
            return new VerAckMessage();
          }
        }

        public uint getChainHeight()
        {
          if(ChainheightCurrent == null)
          {
            throw new InvalidOperationException("Chain height unknown.");
          }

          return (uint)ChainheightCurrent;
        }

        public bool isHandshakeCompleted()
        {
          return VerAckReceived && (VersionMessageRemote != null);
        }
      }
    }

  }
}
