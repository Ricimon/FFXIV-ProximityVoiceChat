using Microsoft.MixedReality.WebRTC;
using ProximityVoiceChat.Log;
using System;
using System.Collections.Generic;

namespace ProximityVoiceChat.WebRTC
{
    public class WebRTCDataChannelHandler(AudioDeviceController audioDeviceController, VoiceRoomManager voiceRoomManager, ILogger logger) : IDisposable
    {
        private string? peerId;
        private PeerConnection? peerConnection;

        private readonly Dictionary<string, Action> stateChangedSubscriptions = [];

        private readonly AudioDeviceController audioDeviceController = audioDeviceController;
        private readonly VoiceRoomManager voiceRoomManager = voiceRoomManager;
        private readonly ILogger logger = logger;

        public void RegisterDataChannel(string ourPeerId, string ourPeerType, Peer peer)
        {
            if (this.peerConnection != null)
            {
                this.logger.Error("Data channel already registered, cannot register again.");
                return;
            }

            this.peerId = peer.PeerId;
            this.peerConnection = peer.PeerConnection;

            if (this.peerConnection != null)
            {
                this.peerConnection.DataChannels.ForEach(channel =>
                {
                    void onStateChanged()
                    {
                        OnStateChange(channel.State);
                    }
                    channel.StateChanged += onStateChanged;
                    this.stateChangedSubscriptions.Add(this.peerId, onStateChanged);
                    channel.MessageReceived += this.OnMessage;
                });
            }

            this.logger.Debug("Data channel registered for peer {0}", this.peerId);
        }

        public void Dispose()
        {
            if (this.peerConnection != null)
            {
                this.peerConnection.DataChannels.ForEach(channel =>
                {
                    if (this.stateChangedSubscriptions.TryGetValue(this.peerId!, out var sub))
                    {
                        channel.StateChanged -= sub;
                        this.stateChangedSubscriptions.Remove(this.peerId!);
                    }
                    channel.MessageReceived -= this.OnMessage;
                });

                this.audioDeviceController.RemoveAudioPlaybackChannel(this.peerId!);
                this.voiceRoomManager.TrackedPlayers.Remove(this.peerId!);
            }
        }

        private void OnStateChange(DataChannel.ChannelState state)
        {
            this.logger.Debug("Peer {0} data channel state changed to {1}", this.peerId!, state);
            if (state == DataChannel.ChannelState.Open)
            {
                this.audioDeviceController.CreateAudioPlaybackChannel(this.peerId!);
                this.voiceRoomManager.TrackedPlayers.Add(this.peerId!, new());
            }
        }

        private void OnMessage(byte[] obj)
        {
            this.logger.Trace("Received data from peer {0}: {1}", this.peerId!, obj);
            if (AudioDeviceController.TryParseAudioSampleBytes(obj, out var sample))
            {
                this.audioDeviceController.AddPlaybackSample(this.peerId!, sample!);
            }
        }

        public interface IFactory
        {
            WebRTCDataChannelHandler CreateHandler();
        }
    }
}
