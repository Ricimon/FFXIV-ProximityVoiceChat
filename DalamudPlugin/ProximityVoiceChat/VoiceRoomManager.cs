using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.MixedReality.WebRTC;
using NAudio.Wave;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.WebRTC;

namespace ProximityVoiceChat;

public sealed class VoiceRoomManager : IDisposable
{
    /// <summary>
    /// When in a public room, this plugin will automatically switch voice rooms when the player changes maps.
    /// This property indicates if the player should be connected to a public voice room.
    /// </summary>
    public bool ShouldBeInRoom { get; private set; }

    public bool InRoom { get; private set; }

    public bool InPublicRoom
    {
        get
        {
#if DEBUG
            return false;
#else
            return InRoom && (this.SignalingChannel?.RoomName?.StartsWith("public") ?? false);
#endif
        }
    }

    public IEnumerable<string> PlayersInVoiceRoom
    {
        get
        {
            if (InRoom)
            {
                if (this.WebRTCManager != null)
                {
                    return this.WebRTCManager.Peers.Keys.Prepend(this.localPlayerFullName ?? "null");
                }
                else
                {
                    return [this.localPlayerFullName ?? "null"];
                }
            }
            else
            {
                return [];
            }
        }
    }

    public SignalingChannel? SignalingChannel { get; private set; }
    public WebRTCManager? WebRTCManager { get; private set; }

    public Dictionary<string, TrackedPlayer> TrackedPlayers { get; } = [];

    private const string PeerType = "player";

    private string? localPlayerFullName;

    private readonly DalamudServices dalamud;
    private readonly Configuration configuration;
    private readonly MapManager mapManager;
    private readonly WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly ILogger logger;

    private readonly LoadConfig loadConfig;
    private readonly CachedSound roomJoinSound;
    private readonly CachedSound roomSelfLeaveSound;
    private readonly CachedSound roomOtherLeaveSound;

    public VoiceRoomManager(
        DalamudServices dalamud,
        Configuration configuration,
        MapManager mapManager,
        WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory,
        IAudioDeviceController audioDeviceController,
        ILogger logger)
    {
        this.dalamud = dalamud;
        this.configuration = configuration;
        this.mapManager = mapManager;
        this.dataChannelHandlerFactory = dataChannelHandlerFactory;
        this.audioDeviceController = audioDeviceController;
        this.logger = logger;

        var configPath = Path.Combine(this.dalamud.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "config.json");
        this.loadConfig = null!;
        if (File.Exists(configPath))
        {
            var configString = File.ReadAllText(configPath);
            try
            {
                this.loadConfig = JsonSerializer.Deserialize<LoadConfig>(configString)!;
            }
            catch (Exception) { }
        }
        if (this.loadConfig == null)
        {
            logger.Warn("Could not load config file at {0}", configPath);
            this.loadConfig = new();
        }

        this.roomJoinSound = new(this.dalamud.PluginInterface.GetResourcePath("join.wav"));
        this.roomOtherLeaveSound = new(this.dalamud.PluginInterface.GetResourcePath("other_leave.wav"));
        this.roomSelfLeaveSound = new(this.dalamud.PluginInterface.GetResourcePath("self_leave.wav"));

        this.dalamud.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        this.SignalingChannel?.Dispose();
        this.WebRTCManager?.Dispose();
        this.dalamud.ClientState.Logout -= OnLogout;
    }

    public void JoinPublicVoiceRoom()
    {
        if (this.ShouldBeInRoom)
        {
            this.logger.Error("Already should be in voice room, ignoring public room join request.");
            return;
        }
        string roomName = this.mapManager.GetCurrentMapPublicRoomName();
        string[]? otherPlayers = this.mapManager.InSharedWorldMap() ? null : GetOtherPlayerNamesInInstance().ToArray();
        JoinVoiceRoom(roomName, string.Empty, otherPlayers);
        this.mapManager.OnMapChanged += ReconnectToCurrentMapPublicRoom;
    }

    public void JoinPrivateVoiceRoom(string roomName, string roomPassword)
    {
        if (this.ShouldBeInRoom)
        {
            this.logger.Error("Already should be in voice room, ignoring private room join request.");
            return;
        }
        JoinVoiceRoom(roomName, roomPassword, null);
    }

    public Task LeaveVoiceRoom(bool autoRejoin)
    {
        if (!autoRejoin)
        {
            this.ShouldBeInRoom = false;
            this.mapManager.OnMapChanged -= ReconnectToCurrentMapPublicRoom;
        }

        if (!this.InRoom)
        {
            return Task.CompletedTask;
        }

        this.logger.Debug("Attempting to leave voice room.");

        this.InRoom = false;
        this.localPlayerFullName = null;

        this.audioDeviceController.AudioRecordingIsRequested = false;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable -= SendAudioSampleToAllPeers;

        if (this.WebRTCManager != null)
        {
            this.WebRTCManager.OnPeerAdded -= OnPeerAdded;
            this.WebRTCManager.OnPeerRemoved -= OnPeerRemoved;
            this.WebRTCManager.Dispose();
            this.WebRTCManager = null;
        }

        if (this.configuration.PlayRoomJoinAndLeaveSounds)
        {
            this.audioDeviceController.PlaySfx(this.roomSelfLeaveSound)
                .ContinueWith(task => this.audioDeviceController.AudioPlaybackIsRequested = false, TaskContinuationOptions.OnlyOnRanToCompletion)
                .SafeFireAndForget(ex =>
                {
                    if (ex is not TaskCanceledException) { this.logger.Error(ex.ToString()); }
                });
        }
        else
        {
            this.audioDeviceController.AudioPlaybackIsRequested = false;
        }

        if (this.SignalingChannel != null)
        {
            this.SignalingChannel.OnConnected -= OnSignalingServerConnected;
            this.SignalingChannel.OnReady -= OnSignalingServerReady;
            this.SignalingChannel.OnDisconnected -= OnSignalingServerDisconnected;
            this.SignalingChannel.OnErrored -= OnSignalingServerDisconnected;
            return this.SignalingChannel.DisconnectAsync();
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    public void PushPlayerAudioState()
    {
        if (this.SignalingChannel == null || !this.SignalingChannel.Connected)
        {
            return;
        }

        ushort audioState = 0;
        if (this.audioDeviceController.MuteMic || this.audioDeviceController.PlayingBackMicAudio)
        {
            audioState |= (ushort)Peer.AudioStateFlags.MicMuted;
        }
        if (this.audioDeviceController.Deafen || this.audioDeviceController.PlayingBackMicAudio)
        {
            audioState |= (ushort)Peer.AudioStateFlags.Deafened;
        }
        this.logger.Trace("Pushing player audio state: {0}", audioState);
        this.SignalingChannel.SendAsync(new SignalMessage.SignalPayload
        {
            action = "update",
            connections = [ new SignalMessage.SignalPayload.Connection
            {
                peerId = this.SignalingChannel.PeerId,
                audioState = audioState,
            }],
        }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    private void OnLogout(int type, int code)
    {
        LeaveVoiceRoom(false);
    }

    private IEnumerable<string> GetOtherPlayerNamesInInstance()
    {
        return this.dalamud.ObjectTable.GetPlayers()
            .Select(p => p.GetPlayerFullName())
            .Where(s => s != null)
            .Where(s => s != this.dalamud.PlayerState.GetLocalPlayerFullName())
            .Cast<string>();
    }

    private void JoinVoiceRoom(string roomName, string roomPassword, string[]? playersInInstance)
    {
        if (this.InRoom)
        {
            this.logger.Error("Already in voice room, ignoring join request.");
            return;
        }

        this.logger.Debug("Attempting to join voice room.");

        var playerName = this.dalamud.PlayerState.GetLocalPlayerFullName();
        if (playerName == null)
        {
#if DEBUG
            playerName = "testPeer14";
            this.logger.Warn("Player name is null. Setting it to {0} for debugging.", playerName);
#else
            this.logger.Error("Player name is null, cannot join voice room.");
            return;
#endif
        }

        this.InRoom = true;
        this.ShouldBeInRoom = true;
        this.localPlayerFullName = playerName;

        this.logger.Trace("Creating SignalingChannel class with peerId {0}", playerName);
        if (this.SignalingChannel == null)
        {
            this.SignalingChannel = new SignalingChannel(playerName,
                PeerType,
                this.loadConfig.signalingServerUrl,
                this.loadConfig.signalingServerToken,
                this.logger,
                true);
        }
        else
        {
            this.SignalingChannel.PeerId = playerName;
        }
        if (this.WebRTCManager == null)
        {
            var options = new WebRTCOptions()
            {
                EnableDataChannel = true,
                DataChannelHandlerFactory = this.dataChannelHandlerFactory,
                TurnServerUrlOverride = this.loadConfig.turnServerUrlOverride,
                TurnServerUsernameOverride = this.loadConfig.turnServerUsernameOverride,
                TurnServerPasswordOverride = this.loadConfig.turnServerPasswordOverride,
            };
            this.WebRTCManager = new WebRTCManager(playerName, PeerType, this.SignalingChannel, options, this.logger, true);
        }

        this.SignalingChannel.OnConnected += OnSignalingServerConnected;
        this.SignalingChannel.OnReady += OnSignalingServerReady;
        this.SignalingChannel.OnDisconnected += OnSignalingServerDisconnected;
        this.SignalingChannel.OnErrored += OnSignalingServerErrored;
        this.WebRTCManager.OnPeerAdded += OnPeerAdded;
        this.WebRTCManager.OnPeerRemoved += OnPeerRemoved;

        this.logger.Debug("Attempting to connect to signaling channel.");
        this.SignalingChannel.ConnectAsync(roomName, roomPassword, playersInInstance).SafeFireAndForget(ex =>
        {
            if (ex is not OperationCanceledException)
            {
                this.logger.Error(ex.ToString());
            }
        });
    }

    private void ReconnectToCurrentMapPublicRoom()
    {
        if (this.ShouldBeInRoom &&
            (!this.InRoom || this.SignalingChannel?.RoomName != this.mapManager.GetCurrentMapPublicRoomName()))
        {
            Task.Run(async () =>
            {
                await this.LeaveVoiceRoom(true);
                // Add an arbitrary delay here as loading a new map can result in a null local player name during load.
                // This delay hopefully allows the game to populate that field before a reconnection attempt happens.
                // Also in some housing districts, the mapId is different after the OnTerritoryChanged event
                await Task.Delay(1000);
                // Accessing the object table must happen on the main thread
                this.dalamud.Framework.Run(() =>
                {
                    var roomName = this.mapManager.GetCurrentMapPublicRoomName();
                    string[]? otherPlayers = this.mapManager.InSharedWorldMap() ? null : GetOtherPlayerNamesInInstance().ToArray();
                    this.JoinVoiceRoom(roomName, string.Empty, otherPlayers);
                }).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
            });
        }
    }

    private void OnSignalingServerConnected()
    {
        this.audioDeviceController.AudioRecordingIsRequested = true;
        this.audioDeviceController.AudioPlaybackIsRequested = true;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable += SendAudioSampleToAllPeers;
        if (this.configuration.PlayRoomJoinAndLeaveSounds)
        {
            this.audioDeviceController.PlaySfx(this.roomJoinSound);
        }
    }

    private void OnSignalingServerReady()
    {
        PushPlayerAudioState();
    }

    private void OnSignalingServerDisconnected()
    {
        LeaveVoiceRoom(false).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
    }

    private void OnSignalingServerErrored()
    {
        LeaveVoiceRoom(false).SafeFireAndForget(ex => this.logger.Error(ex.ToString()));
        this.SignalingChannel?.Dispose();
        this.SignalingChannel = null;
    }

    private void OnPeerAdded(Peer peer, bool isPolite)
    {
        // When joining an occupied room, creating connections to peers already in the room is not polite.
        // Newly joining peers are polite, so play the room join sound for them
        if (isPolite && this.configuration.PlayRoomJoinAndLeaveSounds)
        {
            this.audioDeviceController.PlaySfx(this.roomJoinSound);
        }
    }

    private void OnPeerRemoved(Peer peer)
    {
        if (this.configuration.PlayRoomJoinAndLeaveSounds)
        {
            this.audioDeviceController.PlaySfx(this.roomOtherLeaveSound);
        }
    }

    private void SendAudioSampleToAllPeers(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (this.audioDeviceController.PlayingBackMicAudio)
            {
                return;
            }

            if (this.WebRTCManager == null || this.WebRTCManager.Peers.Count == 0)
            {
                return;
            }

            foreach (var peer in this.WebRTCManager.Peers.Values)
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
