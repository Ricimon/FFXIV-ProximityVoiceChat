using AsyncAwaitBestPractices;
using Dalamud.Plugin.Services;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.WebRTC;
using System;
using System.Collections.Generic;

namespace ProximityVoiceChat;

public class VoiceRoomManager(IClientState clientState, AudioDeviceController audioDeviceController, ILogger logger) : IDisposable
{
    public bool InRoom { get; private set; }

    public List<string> PlayersInVoiceRoom { get; init; } = [];

    private const string Token = "FFXIV-ProximityVoiceChat_Signaling";
    private const string SignalingServerUrl = "http://localhost:3030";
    private const string PeerType = "player";

    private SignalingChannel? signalingChannel;
    private WebRTCManager? webRTCManager;

    private readonly IClientState clientState = clientState;
    private readonly AudioDeviceController audioDeviceController = audioDeviceController;
    private readonly ILogger logger = logger;

    public void Dispose()
    {
        this.signalingChannel?.Dispose();
        this.webRTCManager?.Dispose();
    }

    public void JoinVoiceRoom()
    {
        this.logger.Trace("Attempting to join voice room.");

        var playerName = GetPlayerName();
        if (playerName == null)
        {
            this.logger.Error("Player name is null, cannot join voice room.");
            return;
        }

        if (!PlayersInVoiceRoom.Contains(playerName))
        {
            PlayersInVoiceRoom.Add(playerName);
            InRoom = true;

            this.signalingChannel ??= new SignalingChannel(playerName, PeerType, SignalingServerUrl, Token, this.logger, true);
            var options = new WebRTCOptions()
            {
                EnableDataChannel = true,
                DataChannelHandler = new(this.logger),
            };
            this.webRTCManager ??= new WebRTCManager(playerName, PeerType, this.signalingChannel, options, this.logger, true);

            this.audioDeviceController.AudioRecordingIsRequested = true;
            this.audioDeviceController.AudioPlaybackIsRequested = true;
            this.audioDeviceController.OnAudioRecordingSourceEncodedSample += SendAudioSampleToAllPeers;

            this.logger.Trace("Attempting to connect to signaling channel.");
            this.signalingChannel.ConnectAsync().SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
        }
    }

    public void LeaveVoiceRoom()
    {
        this.logger.Trace("Attempting to leave voice room.");

        var playerName = GetPlayerName();
        if (playerName == null) 
        {
            this.logger.Error("Player name is null, cannot leave voice room.");
            return;
        }

        PlayersInVoiceRoom.Remove(playerName);
        InRoom = false;

        this.audioDeviceController.AudioRecordingIsRequested = false;
        this.audioDeviceController.AudioPlaybackIsRequested = false;
        this.audioDeviceController.OnAudioRecordingSourceEncodedSample -= SendAudioSampleToAllPeers;

        this.signalingChannel?.DisconnectAsync().SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    private string? GetPlayerName()
    {
        var localPlayer = this.clientState.LocalPlayer;
        if (localPlayer == null) 
        {
            this.logger.Error("Local player not found when trying to get player name.");
            return null;
        }
        var homeWorld = localPlayer.HomeWorld.GameData;
        if (homeWorld == null)
        {
            this.logger.Error("Local player home world not found when trying to get player name.");
            return null;
        }

        return localPlayer.Name.TextValue + "@" + homeWorld.Name.RawString;
    }

    private void SendAudioSampleToAllPeers(uint durationRtpUnits, byte[] sample)
    {
        if (this.webRTCManager == null)
        {
            return;
        }

        if (this.audioDeviceController.PlayingBackMicAudio)
        {
            return;
        }

        foreach (var peer in this.webRTCManager.Peers.Values)
        {
            peer.RTCDataChannel.send(sample);
        }
    }
}
