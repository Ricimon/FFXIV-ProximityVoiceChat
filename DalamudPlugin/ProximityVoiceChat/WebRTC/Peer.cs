using SIPSorcery.Net;

namespace ProximityVoiceChat.WebRTC;
public class Peer
{
    public required string PeerId;
    public required string PeerType;
    public bool Polite;
    public required RTCPeerConnection RTCPeerConnection;
    public required RTCDataChannel RTCDataChannel;
    public bool MakingOffer;
    public bool IgnoreOffer;
    public bool IsSettingRemoteAnswerPending;
    public bool CanTrickleIceCandidates;
}
