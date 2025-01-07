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
    public class Player
    {
        public float Distance { get; set; } = float.NaN;
        public float Volume { get; set; } = 1.0f;
    }

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
            return InRoom && (this.SignalingChannel?.RoomName.StartsWith("public") ?? false);
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

    public Dictionary<string, Player> TrackedPlayers { get; } = [];

    private const string PeerType = "player";

    private string? localPlayerFullName;
    private bool isDisposed;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly Configuration configuration;
    private readonly MapChangeHandler mapChangeHandler;
    private readonly WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly ILogger logger;

    private readonly LoadConfig loadConfig;
    private readonly string signalingServerUrl = "http://ffxiv.ricimon.com";
    //private readonly string signalingServerUrl = "http://192.168.1.101:3030";
    private readonly string turnServerUrl = "turn:ffxiv.ricimon.com:3478";
    private readonly PeriodicTimer volumeUpdateTimer = new(TimeSpan.FromMilliseconds(100));
    private readonly SemaphoreSlim frameworkThreadSemaphore = new(1, 1);

    public VoiceRoomManager(IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IFramework framework,
        IObjectTable objectTable,
        Configuration configuration,
        MapChangeHandler mapChangeHandler,
        WebRTCDataChannelHandler.IFactory dataChannelHandlerFactory,
        IAudioDeviceController audioDeviceController,
        ILogger logger)
    {
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.framework = framework;
        this.objectTable = objectTable;
        this.configuration = configuration;
        this.mapChangeHandler = mapChangeHandler;
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
        string roomName = this.mapChangeHandler.GetCurrentMapPublicRoomName();
        this.logger.Debug("Attempting to join public room {0}", roomName);
        JoinVoiceRoom(roomName, string.Empty);
        this.mapChangeHandler.OnMapChanged += ReconnectToCurrentMapPublicRoom;
    }

    public void JoinPrivateVoiceRoom(string roomName, string roomPassword)
    {
        if (this.ShouldBeInRoom)
        {
            this.logger.Error("Already should be in voice room, ignoring private room join request.");
            return;
        }
        JoinVoiceRoom(roomName, roomPassword);
    }

    public Task LeaveVoiceRoom(bool autoRejoin)
    {
        if (!autoRejoin)
        {
            this.ShouldBeInRoom = false;
            this.mapChangeHandler.OnMapChanged -= ReconnectToCurrentMapPublicRoom;
        }

        if (!this.InRoom)
        {
            return Task.CompletedTask;
        }

        this.logger.Debug("Attempting to leave voice room.");

        this.InRoom = false;
        this.localPlayerFullName = null;

        this.audioDeviceController.AudioRecordingIsRequested = false;
        this.audioDeviceController.AudioPlaybackIsRequested = false;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable -= SendAudioSampleToAllPeers;

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

    private void UpdatePlayerVolumes()
    {
        try
        {
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
            if (this.WebRTCManager.Peers.Select(kv => kv.Value.PeerConnection.DataChannels.Count).All(c => c == 0))
            {
                return;
            }

            var players = this.objectTable.Where(go => go.ObjectKind == ObjectKind.Player).OfType<IPlayerCharacter>();
            foreach (var player in players)
            {
                var playerName = player.GetPlayerFullName();
                if (playerName != null &&
                    this.WebRTCManager.Peers.TryGetValue(playerName, out var peer) &&
                    peer.PeerConnection.DataChannels.Count > 0)
                {
                    var distance = Vector3.Distance(this.clientState.LocalPlayer.Position, player.Position);
                    var deathMute = this.configuration.MuteDeadPlayers && player.IsDead;
                    float volume;
                    if (deathMute)
                    {
                        volume = 0;
                        //this.logger.Debug("Player {0} is dead, setting volume to {1}", peer.PeerId, volume);
                    }
                    else
                    {
                        volume = CalculateVolume(distance);
                        //this.logger.Debug("Player {0} is {1} units away, setting volume to {2}", peer.PeerId, distance, volume);
                    }

                    this.audioDeviceController.SetChannelVolume(peer.PeerId, volume * this.configuration.MasterVolume);
                    if (this.TrackedPlayers.TryGetValue(playerName, out var tp))
                    {
                        tp.Distance = distance;
                        tp.Volume = volume;
                    }
                }
            }
        }
        finally
        {
            this.frameworkThreadSemaphore.Release();
        }
    }

    private float CalculateVolume(float distance)
    {
        var minDistance = this.configuration.FalloffModel.MinimumDistance;
        var maxDistance = this.configuration.FalloffModel.MaximumDistance;
        var falloffFactor = this.configuration.FalloffModel.FalloffFactor;
        double volume;
        try
        {
            double scale;
            switch (this.configuration.FalloffModel.Type)
            {
                case AudioFalloffModel.FalloffType.None:
                    volume = 1.0;
                    break;
                case AudioFalloffModel.FalloffType.InverseDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    scale = Math.Pow((maxDistance - distance) / (maxDistance - minDistance), distance / maxDistance);
                    volume = minDistance / (minDistance + falloffFactor * (distance - minDistance)) * scale;
                    break;
                case AudioFalloffModel.FalloffType.ExponentialDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    scale = Math.Pow((maxDistance - distance) / (maxDistance - minDistance), distance / maxDistance);
                    volume = Math.Pow(distance / minDistance, -falloffFactor) * scale;
                    break;
                case AudioFalloffModel.FalloffType.LinearDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    volume = 1 - falloffFactor * (distance - minDistance) / (maxDistance - minDistance);
                    break;
                default:
                    volume = 1.0;
                    break;
            }
        }
        catch (Exception e) when (e is DivideByZeroException or ArgumentException)
        {
            volume = 1.0;
        }
        volume = Math.Clamp(volume, 0.0, 1.0);
        return (float)volume;
    }

    private void JoinVoiceRoom(string roomName, string roomPassword)
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
            this.logger.Error("Player name is null, cannot join voice room.");
            return;
        }

        this.InRoom = true;
        this.ShouldBeInRoom = true;
        this.localPlayerFullName = playerName;

        this.logger.Trace("Creating SignalingChannel class with peerId {0}", playerName);
        this.SignalingChannel ??= new SignalingChannel(playerName,
            PeerType,
            this.loadConfig.signalingServerOverride ?? this.signalingServerUrl,
            this.loadConfig.signalingServerToken ?? string.Empty,
            this.logger,
            true);
        var options = new WebRTCOptions()
        {
            EnableDataChannel = true,
            DataChannelHandlerFactory = this.dataChannelHandlerFactory,
            TurnServerUrl = this.loadConfig.turnServerOverride ?? this.turnServerUrl,
            TurnUsername = this.loadConfig.turnUsername,
            TurnPassword = this.loadConfig.turnPassword,
        };
        this.WebRTCManager ??= new WebRTCManager(playerName, PeerType, this.SignalingChannel, options, this.logger, true);

        this.SignalingChannel.OnConnected += OnSignalingServerConnected;
        this.SignalingChannel.OnReady += OnSignalingServerReady;
        this.SignalingChannel.OnDisconnected += OnSignalingServerDisconnected;
        this.SignalingChannel.OnErrored += OnSignalingServerErrored;

        this.logger.Debug("Attempting to connect to signaling channel.");
        this.SignalingChannel.ConnectAsync(roomName, roomPassword).SafeFireAndForget(ex =>
        {
            if (ex is not OperationCanceledException)
            {
                this.logger.Error(ex.ToString());
            }
        });
    }

    private void ReconnectToCurrentMapPublicRoom(string publicRoomName)
    {
        if (this.ShouldBeInRoom &&
            (!this.InRoom || this.SignalingChannel?.RoomName != publicRoomName))
        {
            Task.Run(async () =>
            {
                await this.LeaveVoiceRoom(true);
                this.JoinVoiceRoom(publicRoomName, string.Empty);
            });
        }
    }

    private void OnSignalingServerConnected()
    {
        this.audioDeviceController.AudioRecordingIsRequested = true;
        this.audioDeviceController.AudioPlaybackIsRequested = true;
        this.audioDeviceController.OnAudioRecordingSourceDataAvailable += SendAudioSampleToAllPeers;
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

    private void SendAudioSampleToAllPeers(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (this.WebRTCManager == null)
            {
                return;
            }

            if (this.audioDeviceController.PlayingBackMicAudio)
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
