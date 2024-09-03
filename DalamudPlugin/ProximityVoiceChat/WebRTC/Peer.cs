using Microsoft.MixedReality.WebRTC;
using SIPSorcery.Net;

namespace ProximityVoiceChat.WebRTC;
public class Peer
{
    public required string PeerId;
    public required string PeerType;
    public bool Polite;
    public required PeerConnection PeerConnection;
    //public required RTCDataChannel RTCDataChannel;
    public WebRTCDataChannelHandler? DataChannelHandler;
    public bool MakingOffer;
    public bool IgnoreOffer;
    public bool IsSettingRemoteAnswerPending;
    public bool CanTrickleIceCandidates;
}
