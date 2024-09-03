using AsyncAwaitBestPractices;
using Dalamud.Game.ClientState.Objects.Enums;
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
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityVoiceChat;

public class VoiceRoomManager : IDisposable
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
    private WebRTCManager? webRTCManager;

    private bool isDisposed;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory;
    private readonly AudioDeviceController audioDeviceController;
    private readonly ILogger logger;

    private readonly PeriodicTimer volumeUpdateTimer = new(TimeSpan.FromMilliseconds(200));

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

    public VoiceRoomManager(IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IFramework framework,
        IObjectTable objectTable,
        WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory,
        AudioDeviceController audioDeviceController,
        ILogger logger)
    {
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.framework = framework;
        this.objectTable = objectTable;
        this.dataChannelHandlerFactory = dataChannelHandlerFactory;
        this.audioDeviceController = audioDeviceController;
        this.logger = logger;

        Task.Run(async delegate
        {
            while (await this.volumeUpdateTimer.WaitForNextTickAsync())
            {
                if (isDisposed)
                {
                    return;
                }
                this.framework.RunOnFrameworkThread(UpdatePlayerVolumes).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
            }
        }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    public void Dispose()
    {
        isDisposed = true;
        this.signalingChannel?.Dispose();
        this.webRTCManager?.Dispose();
        this.volumeUpdateTimer.Dispose();
        GC.SuppressFinalize(this);
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
        this.webRTCManager ??= new WebRTCManager(playerName, PeerType, this.signalingChannel, options, this.logger, true);

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

    private void UpdatePlayerVolumes()
    {
        // Use polling to set individual channel volumes.
        // The search is done by iterating through all GameObjects and finding any connected players out of them,
        // so we reset all players volumes before calculating any volumes in case the players cannot be found.
        this.audioDeviceController.ResetAllChannelsVolume();

        // Conditions where volume is impossible/unnecessary to calculate
        if (this.clientState.LocalPlayer == null)
        {
            return;
        }
        if (this.webRTCManager == null)
        {
            return;
        }
        if (this.webRTCManager.Peers.Select(kv => kv.Value.PeerConnection.DataChannels.Count).All(c => c == 0))
        {
            return;
        }

        var players = this.objectTable.Where(go => go.ObjectKind == ObjectKind.Player).OfType<IPlayerCharacter>();
        foreach (var player in players)
        {
            var playerName = GetPlayerName(player);
            if (playerName != null &&
                this.webRTCManager.Peers.TryGetValue(playerName, out var peer) &&
                peer.PeerConnection.DataChannels.Count > 0)
            {
                var volume = 1.0f;
                var distance = Vector3.Distance(this.clientState.LocalPlayer.Position, player.Position);
                var nearThreshold = 1.0f;
                if (distance > nearThreshold)
                {
                    volume = 1.0f - (distance - nearThreshold) / 10.0f;
                    volume = Math.Clamp(volume, 0, 1);
                }
                this.logger.Debug("Player {0} is {1} units away, setting volume to {2}", peer.PeerId, distance, volume);
                this.audioDeviceController.SetChannelVolume(peer.PeerId, volume);
            }
        }
    }

    private string? GetLocalPlayerName()
    {
        var localPlayer = this.clientState.LocalPlayer;
        if (localPlayer == null)
        {
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
