using Microsoft.MixedReality.WebRTC;
using System;

namespace ProximityVoiceChat.WebRTC;

public class Peer
{
    public required string PeerId;
    public required string PeerType;
    public bool Polite;
    public required PeerConnection PeerConnection;
    public WebRTCDataChannelHandler? DataChannelHandler;
    public bool MakingOffer;
    public bool IgnoreOffer;
    public bool IsSettingRemoteAnswerPending;
    public bool CanTrickleIceCandidates;

    [Flags]
    public enum AudioStateFlags : ushort
    {
        Default = 0,
        MicMuted = 1 << 0,
        Deafened = 1 << 1,
    }
    public AudioStateFlags AudioState;
}
