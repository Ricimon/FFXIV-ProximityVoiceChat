using SIPSorcery.Net;

namespace ProximityVoiceChat.WebRTC
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public class SignalMessage
    {
        public class SignalPayload
        {
            public class Connection
            {
                public string socketId;
                public string peerId;
                public string peerType;
            }

            public class IcePayload
            {
                public string candidate;
                public string sdpMid;
                public ushort sdpMLineIndex;
                public string foundation;
                public RTCIceComponent component;
                public uint priority;
                public string address;
                public RTCIceProtocol protocol;
                public ushort port;
                public RTCIceCandidateType type;
                public RTCIceTcpCandidateType tcpType;
                public string relatedAddress;
                public ushort relatedPort;
                public string usernameFragment;
            }

            public string action;
            public Connection[] connections;
            public bool bePolite;
            public RTCSessionDescriptionInit sdp;
            public IcePayload ice;
        }

        public string from;
        public string target;
        public SignalPayload payload;
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
}
