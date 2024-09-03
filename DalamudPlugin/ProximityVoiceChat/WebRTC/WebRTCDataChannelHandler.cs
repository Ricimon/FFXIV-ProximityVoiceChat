using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Microsoft.MixedReality.WebRTC;
using ProximityVoiceChat.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ProximityVoiceChat.WebRTC
{
    public class WebRTCDataChannelHandler(AudioDeviceController audioDeviceController, IClientState clientState, IObjectTable objectTable, ILogger logger) : IDisposable
    {
        private string? peerId;
        private PeerConnection? peerConnection;
        //private RTCDataChannel? channel;

        private readonly Dictionary<string, Action> stateChangedSubscriptions = [];

        private readonly AudioDeviceController audioDeviceController = audioDeviceController;
        private readonly IClientState clientState = clientState;
        private readonly IObjectTable objectTable = objectTable;
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
            //this.channel = peer.RTCDataChannel;

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

            //if (this.channel != null)
            //{
            //    this.channel.onopen += this.OnOpen;
            //    this.channel.onmessage += this.OnMessage;
            //    this.channel.onclose += this.OnClose;
            //}
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
            }

            //if (this.channel != null)
            //{
            //    this.channel.onopen -= this.OnOpen;
            //    this.channel.onmessage -= this.OnMessage;
            //    this.channel.onclose -= this.OnClose;
            //}
        }

        private void OnStateChange(DataChannel.ChannelState state)
        {
            this.logger.Debug("Peer {0} data channel state changed to {1}", this.peerId!, state);
            if (state == DataChannel.ChannelState.Open)
            {
                this.audioDeviceController.CreateAudioPlaybackChannel(this.peerId!);
            }
        }

        private void OnMessage(byte[] obj)
        {
            this.logger.Trace("Received data from peer {0}: {1}", this.peerId!, obj);
            if (AudioDeviceController.TryParseAudioSampleBytes(obj, out var sample))
            {
                var overworldPlayer = this.objectTable
                    .Where(go => go.ObjectKind == ObjectKind.Player)
                    .OfType<IPlayerCharacter>()
                    .Where(pc => VoiceRoomManager.GetPlayerName(pc) == this.peerId)
                    .FirstOrDefault();
                var volume = 1.0f;
                if (overworldPlayer != null && this.clientState.LocalPlayer != null)
                {
                    var distance = Vector3.Distance(this.clientState.LocalPlayer.Position, overworldPlayer.Position);
                    var nearThreshold = 1.0f;
                    if (distance > nearThreshold)
                    {
                        volume = 1.0f - (distance - nearThreshold) / 10.0f;
                        volume = Math.Clamp(volume, 0, 1);
                    }
                }
                this.audioDeviceController.AddPlaybackSample(this.peerId!, sample!, volume);
            }
        }

        //private void OnOpen()
        //{
            
        //}

        //private void OnMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        //{
        //    this.logger.Trace("Received data: {0}", data);
        //}

        //private void OnClose()
        //{
        //    this.logger.Debug("Data channel closed.");
        //    Dispose();
        //}

        public interface IFactory
        {
            WebRTCDataChannelHandler CreateHandler();
        }
    }
}
