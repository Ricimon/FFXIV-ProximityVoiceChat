using AsyncAwaitBestPractices;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.MixedReality.WebRTC;
using NAudio.Wave;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.WebRTC;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProximityVoiceChat;

public class VoiceRoomManager(
    IDalamudPluginInterface pluginInterface,
    IClientState clientState,
    WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory,
    AudioDeviceController audioDeviceController,
    ILogger logger) : IDisposable
{
    public bool InRoom { get; private set; }

    public IEnumerable<string> PlayersInVoiceRoom
    {
        get
        {
            if (InRoom)
            {
                if (this.webRTCManager != null)
                {
                    return this.webRTCManager.Peers.Keys.Prepend(GetLocalPlayerName() ?? "null");
                }
                else
                {
                    return [GetLocalPlayerName() ?? "null"];
                }
            }
            else
            {
                return [];
            }
        }
    }

    private const string Token = "FFXIV-ProximityVoiceChat_Signaling";
    //private const string SignalingServerUrl = "http://ffxiv.ricimon.com";
    private const string SignalingServerUrl = "http://192.168.1.101:3030";
    private const string PeerType = "player";

    private SignalingChannel? signalingChannel;
    private WebRTCManager_WindowsMR? webRTCManager;

    private readonly IDalamudPluginInterface pluginInterface = pluginInterface;
    private readonly IClientState clientState = clientState;
    private readonly WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory = dataChannelHandlerFactory;
    private readonly AudioDeviceController audioDeviceController = audioDeviceController;
    private readonly ILogger logger = logger;

    public static string? GetPlayerName(IPlayerCharacter playerCharacter)
    {
        string playerName = playerCharacter.Name.TextValue;
        var homeWorld = playerCharacter.HomeWorld.GameData;
        if (homeWorld != null)
        {
            playerName += $"@{homeWorld.Name.RawString}";
        }

        return playerName;
    }


    public void Dispose()
    {
        this.signalingChannel?.Dispose();
        this.webRTCManager?.Dispose();
    }

    public void JoinVoiceRoom()
    {
        if (this.InRoom)
        {
            this.logger.Error("Already in voice room, ignoring join request.");
            return;
        }

        this.logger.Debug("Attempting to join voice room.");

        var playerName = GetLocalPlayerName();
        if (playerName == null)
        {
            this.logger.Error("Player name is null, cannot join voice room.");
            return;
        }

        InRoom = true;

        this.logger.Trace("Creating SignalingChannel class with peerId {0}", playerName);
        this.signalingChannel ??= new SignalingChannel(playerName, PeerType, SignalingServerUrl, Token, this.logger, true);
        var options = new WebRTCOptions()
        {
            EnableDataChannel = true,
            DataChannelHandlerFactory = this.dataChannelHandlerFactory,
        };
        this.webRTCManager ??= new WebRTCManager_WindowsMR(playerName, PeerType, this.signalingChannel, options, this.logger, true);

        this.signalingChannel.OnConnected += OnSignalingServerConnected;
        this.signalingChannel.OnDisconnected += OnSignalingServerDisconnected;

        this.logger.Debug("Attempting to connect to signaling channel.");
        this.signalingChannel.ConnectAsync().SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    public void LeaveVoiceRoom()
    {
        if (!this.InRoom)
        {
            return;
        }

        this.logger.Trace("Attempting to leave voice room.");

        var playerName = GetLocalPlayerName();
        if (playerName == null) 
        {
            this.logger.Error("Player name is null, cannot leave voice room.");
            return;
        }

        InRoom = false;

        this.audioDeviceController.AudioRecordingIsRequested = false;
        this.audioDeviceController.AudioPlaybackIsRequested = false;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable -= SendAudioSampleToAllPeers;

        if (this.signalingChannel != null)
        {
            this.signalingChannel.OnConnected -= OnSignalingServerConnected;
            this.signalingChannel.OnDisconnected -= OnSignalingServerDisconnected;
            if (this.signalingChannel.Connected)
            {
                this.signalingChannel?.DisconnectAsync().SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
            }
        }
    }

    private string? GetLocalPlayerName()
    {
        var localPlayer = this.clientState.LocalPlayer;
        if (localPlayer == null)
        {
            this.logger.Error("Local player not found when trying to get player name.");
            return null;
        }
        return GetPlayerName(localPlayer);

    }

    private void OnSignalingServerConnected()
    {
        this.audioDeviceController.AudioRecordingIsRequested = true;
        this.audioDeviceController.AudioPlaybackIsRequested = true;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable += SendAudioSampleToAllPeers;
    }

    private void OnSignalingServerDisconnected()
    {
        LeaveVoiceRoom();
    }

    private void SendAudioSampleToAllPeers(object? sender, WaveInEventArgs e)
    {
        try
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
                if (peer.PeerConnection.DataChannels.Count > 0)
                {
                    var dataChannel = peer.PeerConnection.DataChannels[0];
                    if (dataChannel.State == DataChannel.ChannelState.Open)
                    {
                        dataChannel.SendMessage(AudioDeviceController.ConvertAudioSampleToByteArray(e));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Error(ex.ToString());
        }
    }
}
