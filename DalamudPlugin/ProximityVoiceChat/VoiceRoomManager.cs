using AsyncAwaitBestPractices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.MixedReality.WebRTC;
using NAudio.Wave;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.WebRTC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityVoiceChat;

public class VoiceRoomManager : IDisposable
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
    private bool isDisposed;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly Configuration configuration;
    private readonly Spatializer spatializer;
    private readonly MapManager mapManager;
    private readonly WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly ILogger logger;

    private readonly LoadConfig loadConfig;
    private readonly CachedSound roomJoinSound;
    private readonly CachedSound roomSelfLeaveSound;
    private readonly CachedSound roomOtherLeaveSound;
    private readonly PeriodicTimer volumeUpdateTimer = new(TimeSpan.FromMilliseconds(100));
    private readonly SemaphoreSlim frameworkThreadSemaphore = new(1, 1);

    public VoiceRoomManager(IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IFramework framework,
        IObjectTable objectTable,
        IGameGui gameGui,
        Configuration configuration,
        Spatializer audioSpatializer,
        MapManager mapManager,
        WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory,
        IAudioDeviceController audioDeviceController,
        ILogger logger)
    {
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.framework = framework;
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.configuration = configuration;
        this.spatializer = audioSpatializer;
        this.mapManager = mapManager;
        this.dataChannelHandlerFactory = dataChannelHandlerFactory;
        this.audioDeviceController = audioDeviceController;
        this.logger = logger;

        var configPath = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty, "config.json");
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

        this.roomJoinSound = new(this.pluginInterface.GetResourcePath("join.wav"));
        this.roomOtherLeaveSound = new(this.pluginInterface.GetResourcePath("other_leave.wav"));
        this.roomSelfLeaveSound = new(this.pluginInterface.GetResourcePath("self_leave.wav"));

        Task.Run(async delegate
        {
            while (await this.volumeUpdateTimer.WaitForNextTickAsync())
            {
                await frameworkThreadSemaphore.WaitAsync();
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
        this.SignalingChannel?.Dispose();
        this.WebRTCManager?.Dispose();
        this.volumeUpdateTimer.Dispose();
        GC.SuppressFinalize(this);
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
        if (this.SignalingChannel == null || !this.SignalingChannel.Ready)
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

    private IEnumerable<IPlayerCharacter> GetPlayersInInstance()
    {
        return this.objectTable.Where(go => go.ObjectKind == ObjectKind.Player).OfType<IPlayerCharacter>();
    }

    private IEnumerable<string> GetOtherPlayerNamesInInstance()
    {
        return GetPlayersInInstance()
            .Select(p => p.GetPlayerFullName())
            .Where(s => s != null)
            .Where(s => s != this.clientState.GetLocalPlayerFullName())
            .Cast<string>();
    }

    private void UpdatePlayerVolumes()
    {
        try
        {
            // Debug stuff
            unsafe
            {
                var renderingCamera = *FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera;
                // renderingCamera.Rotation is always (0,0,0,1)
                // renderingCamera.LookAtVector doesn't seem accurate
                // renderingCamera.Vector_1 seems to only change with camera pitch
                // renderingCamera.Position = renderingCamera.Object.Position
                // https://github.com/NotNite/Linkpearl/blob/main/Linkpearl/Plugin.cs
                var lookAtVector = new Vector3(renderingCamera.ViewMatrix.M13, renderingCamera.ViewMatrix.M23, renderingCamera.ViewMatrix.M33);
                //Matrix4x4.Decompose(renderingCamera.ViewMatrix, out var scale, out var rotation, out var position);
                this.logger.Debug("PlayerPosition {0}, CameraPosition {1}, LookAtVector {2}, CameraUp {3}",
                    (this.clientState.LocalPlayer?.Position ?? Vector3.Zero).ToString("F2", null),
                    renderingCamera.Position.ToString("F2", null),  // useful
                    lookAtVector.ToString("F2", null),  // useful
                    renderingCamera.Vector_1.ToString("F2", null)); // not useful
            }

            // Use polling to set individual channel volumes.
            // The search is done by iterating through all GameObjects and finding any connected players out of them,
            // so we reset all players volumes before calculating any volumes in case the players cannot be found.
            var defaultVolume = (InPublicRoom || this.configuration.MuteOutOfMapPlayers) ? 0.0f : 1.0f;
            this.audioDeviceController.ResetAllChannelsVolume(defaultVolume * this.configuration.MasterVolume);
            foreach (var tp in this.TrackedPlayers.Values)
            {
                tp.Distance = float.NaN;
                tp.Volume = defaultVolume;
            }

            // Conditions where volume is impossible/unnecessary to calculate
            if (this.clientState.LocalPlayer == null)
            {
                return;
            }
            if (this.WebRTCManager == null)
            {
                return;
            }
            if (this.WebRTCManager.Peers.All(kv => kv.Value.PeerConnection.DataChannels.Count == 0))
            {
                return;
            }

            var thisTick = Environment.TickCount;

            foreach (var player in GetPlayersInInstance())
            {
                var playerName = player.GetPlayerFullName();
                if (playerName != null &&
                    this.WebRTCManager.Peers.TryGetValue(playerName, out var peer) &&
                    peer.PeerConnection.DataChannels.Count > 0)
                {
                    var trackedPlayer = this.TrackedPlayers.TryGetValue(playerName, out var tp) ? tp : null;
                    this.spatializer.CalculateSpatialValues(player, trackedPlayer, thisTick,
                       out var leftVolume, out var rightVolume, out var distance);

                    leftVolume *= this.configuration.MasterVolume;
                    rightVolume *= this.configuration.MasterVolume;

                    this.audioDeviceController.SetChannelVolume(peer.PeerId, leftVolume, rightVolume);
                    if (trackedPlayer != null)
                    {
                        trackedPlayer.Distance = distance;
                        trackedPlayer.Volume = (leftVolume + rightVolume) * 0.5f;
                    }
                }
            }
        }
        finally
        {
            this.frameworkThreadSemaphore.Release();
        }
    }

    private void JoinVoiceRoom(string roomName, string roomPassword, string[]? playersInInstance)
    {
        if (this.InRoom)
        {
            this.logger.Error("Already in voice room, ignoring join request.");
            return;
        }

        this.logger.Debug("Attempting to join voice room.");

        var playerName = this.clientState.GetLocalPlayerFullName();
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
        this.SignalingChannel ??= new SignalingChannel(playerName,
            PeerType,
            this.loadConfig.signalingServerUrl,
            this.loadConfig.signalingServerToken,
            this.logger,
            true);
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
                this.framework.Run(() =>
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
