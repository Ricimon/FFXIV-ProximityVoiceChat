using ProximityVoiceChat.Log;
using SIPSorcery.Net;
using System;

namespace ProximityVoiceChat.WebRTC
{
    public class WebRTCDataChannelHandler(ILogger logger) : IDisposable
    {
        private RTCDataChannel? channel;

        private readonly ILogger logger = logger;

        public void RegisterDataChannel(string ourPeerId, string ourPeedType, Peer peer)
        {
            if (channel != null)
            {
                this.logger.Error("Data channel already registered, cannot register again.");
                return;
            }

            var peerId = peer.PeerId;
            this.channel = peer.RTCDataChannel;

            this.channel.onopen += this.OnOpen;
            this.channel.onmessage += this.OnMessage;
            this.channel.onclose += this.OnClose;
        }

        public void Dispose()
        {
            if (this.channel != null)
            {
                this.channel.onopen -= this.OnOpen;
                this.channel.onmessage -= this.OnMessage;
                this.channel.onclose -= this.OnClose;
            }
        }

        private void OnOpen()
        {
            
        }

        private void OnMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        {
            this.logger.Trace("Received data: {0}", data);
        }

        private void OnClose()
        {
            this.logger.Debug("Data channel closed.");
            Dispose();
        }
    }
}
