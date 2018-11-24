using System;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    partial class Peer
    {
      class PeerHandshakeManager
      {
        Peer Peer;

        bool VerAckReceived;
        bool VersionReceived;
        
        public PeerHandshakeManager(Peer peer)
        {
          Peer = peer;
        }

        public async Task ProcessResponseToVersionMessageAsync(NetworkMessage messageRemote)
        {
          switch (messageRemote.Command)
          {
            case "verack":
              VerAckReceived = true;
              break;

            case "version":
              VersionMessage versionMessageRemote = new VersionMessage(messageRemote.Payload);
              VersionReceived = true;
              await ProcessVersionMessageRemoteAsync(versionMessageRemote).ConfigureAwait(false);
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(messageRemote.Payload);
              throw new NetworkException(string.Format("Peer rejected handshake: '{0}'", rejectMessage.RejectionReason));

            default:
              throw new NetworkException(string.Format("Handshake aborted: Received improper message '{0}' during handshake session.", messageRemote.Command));
          }
        }
        async Task ProcessVersionMessageRemoteAsync(VersionMessage versionMessageRemote)
        {
          ValidateVersionRemoteAsync(versionMessageRemote, out string rejectionReason);
          if (rejectionReason != "")
          {
            await Peer.SendMessageAsync(new RejectMessage("version", RejectMessage.RejectCode.OBSOLETE, rejectionReason)).ConfigureAwait(false);
            throw new NetworkException("Remote peer rejected: " + rejectionReason);
          }
          await Peer.SendMessageAsync(new VerAckMessage());
        }
        void ValidateVersionRemoteAsync(VersionMessage versionMessageRemote, out string rejectionReason)
        {
          rejectionReason = "";

          if (versionMessageRemote.ProtocolVersion < ProtocolVersion)
          {
            rejectionReason = string.Format("Outdated version '{0}', minimum expected version is '{1}'.", versionMessageRemote.ProtocolVersion, ProtocolVersion);
          }

          if (!((ServiceFlags)versionMessageRemote.NetworkServicesLocal).HasFlag(NetworkServicesRemoteRequired))
          {
            rejectionReason = string.Format("Network services '{0}' do not meet requirement '{1}'.", versionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
          }

          if (versionMessageRemote.UnixTimeSeconds - GetUnixTimeSeconds() > 2 * 60 * 60)
          {
            rejectionReason = string.Format("Unix time '{0}' more than 2 hours in the future compared to local time '{1}'.", versionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
          }

          if (versionMessageRemote.Nonce == Nonce)
          {
            rejectionReason = string.Format("Duplicate Nonce '{0}'.", Nonce);
          }
          
        }

        public bool isHandshakeCompleted()
        {
          return VerAckReceived && VersionReceived;
        }
      }
    }

  }
}
