using AsyncAwaitBestPractices;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Input;
using ProximityVoiceChat.Log;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace ProximityVoiceChat;

public class Spatializer : IDisposable
{
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IFramework framework;
    private readonly Configuration configuration;
    private readonly VoiceRoomManager voiceRoomManager;
    private readonly IAudioDeviceController audioDeviceController;
    private readonly ILogger logger;

    private readonly PeriodicTimer updateTimer = new(TimeSpan.FromMilliseconds(100));
    private readonly SemaphoreSlim frameworkThreadSemaphore = new(1, 1);

    private bool isDisposed;

    public Spatializer(IClientState clientState,
        IObjectTable objectTable,
        IFramework framework,
        Configuration configuration,
        VoiceRoomManager voiceRoomManager,
        IAudioDeviceController audioDeviceController,
        ILogger logger)
    {
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.framework = framework;
        this.configuration = configuration;
        this.voiceRoomManager = voiceRoomManager;
        this.audioDeviceController = audioDeviceController;
        this.logger = logger;
    }

    public void StartUpdateLoop()
    {
        Task.Run(async delegate
        {
            while (await this.updateTimer.WaitForNextTickAsync())
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
        this.updateTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void UpdatePlayerVolumes()
    {
        try
        {

            // Use polling to set individual channel volumes.
            // The search is done by iterating through all GameObjects and finding any connected players out of them,
            // so we reset all players volumes before calculating any volumes in case the players cannot be found.
            var defaultVolume = (this.voiceRoomManager.InPublicRoom || this.configuration.MuteOutOfMapPlayers) ? 0.0f : 1.0f;
            this.audioDeviceController.ResetAllChannelsVolume(defaultVolume * this.configuration.MasterVolume);
            foreach (var tp in this.voiceRoomManager.TrackedPlayers.Values)
            {
                tp.Distance = float.NaN;
                tp.Volume = defaultVolume;
            }

            //DebugStuff();

            // Conditions where volume is impossible/unnecessary to calculate
            if (this.clientState.LocalPlayer == null)
            {
                return;
            }
            if (this.voiceRoomManager.WebRTCManager == null)
            {
                return;
            }
            if (!this.voiceRoomManager.WebRTCManager.Peers.Any(kv => kv.Value.PeerConnection.DataChannels.Count > 0))
            {
                return;
            }

            var thisTick = Environment.TickCount;

            foreach (var player in this.objectTable.GetPlayers())
            {
                var playerName = player.GetPlayerFullName();
                if (playerName != null &&
                    this.voiceRoomManager.WebRTCManager.Peers.TryGetValue(playerName, out var peer) &&
                    peer.PeerConnection.DataChannels.Count > 0)
                {
                    var trackedPlayer = this.voiceRoomManager.TrackedPlayers.TryGetValue(playerName, out var tp) ? tp : null;
                    CalculateSpatialValues(player, trackedPlayer, thisTick,
                       out var leftVolume, out var rightVolume, out var distance, out var volume);

                    leftVolume *= this.configuration.MasterVolume;
                    rightVolume *= this.configuration.MasterVolume;
                    this.audioDeviceController.SetChannelVolume(peer.PeerId, leftVolume, rightVolume);

                    if (trackedPlayer != null)
                    {
                        trackedPlayer.Distance = distance;
                        trackedPlayer.Volume = volume;
                    }
                }
            }
        }
        finally
        {
            this.frameworkThreadSemaphore.Release();
        }
    }

    private void CalculateSpatialValues(
        IPlayerCharacter otherPlayer,
        TrackedPlayer? otherTrackedPlayer,
        int thisTick,
        out float leftVolume,
        out float rightVolume,
        out float distance,
        out float volume)
    {
        Vector3 toTarget;
        if (this.clientState.LocalPlayer != null)
        {
            toTarget = otherPlayer.Position - this.clientState.LocalPlayer.Position;
        }
        else
        {
            toTarget = Vector3.Zero;
        }
        distance = toTarget.Length();
        var deathMute = this.configuration.MuteDeadPlayers;

        if (this.configuration.UnmuteAllIfDead && (this.clientState.LocalPlayer?.IsDead ?? false))
        {
            deathMute = false;
        }
        else if (otherTrackedPlayer != null)
        {
            if (!otherPlayer.IsDead)
            {
                otherTrackedPlayer.LastTickFoundAlive = thisTick;
                deathMute = false;
            }
            else if (deathMute &&
                otherTrackedPlayer.LastTickFoundAlive.HasValue &&
                thisTick - otherTrackedPlayer.LastTickFoundAlive < this.configuration.MuteDeadPlayersDelayMs)
            {
                deathMute = false;
            }
        }
        else
        {
            deathMute = deathMute && otherPlayer.IsDead;
        }

        if (deathMute)
        {
            volume = leftVolume = rightVolume = 0;
            //this.logger.Debug("Player {0} is dead, setting volume to {1}", peer.PeerId, volume);
        }
        else
        {
            volume = leftVolume = rightVolume = CalculateVolume(distance);
            //this.logger.Debug("Player {0} is {1} units away, setting volume to {2}", peer.PeerId, distance, volume);

            if (this.configuration.EnableSpatialization)
            {
                SpatializeVolume(volume, toTarget, out leftVolume, out rightVolume);
            }
        }
    }

    private float CalculateVolume(float distance)
    {
        var minDistance = this.configuration.FalloffModel.MinimumDistance;
        var maxDistance = this.configuration.FalloffModel.MaximumDistance;
        var falloffFactor = this.configuration.FalloffModel.FalloffFactor;
        float volume;
        try
        {
            float scale;
            switch (this.configuration.FalloffModel.Type)
            {
                case AudioFalloffModel.FalloffType.None:
                    volume = 1.0f;
                    break;
                case AudioFalloffModel.FalloffType.InverseDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    scale = MathF.Pow((maxDistance - distance) / (maxDistance - minDistance), distance / maxDistance);
                    volume = minDistance / (minDistance + falloffFactor * (distance - minDistance)) * scale;
                    break;
                case AudioFalloffModel.FalloffType.ExponentialDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    scale = MathF.Pow((maxDistance - distance) / (maxDistance - minDistance), distance / maxDistance);
                    volume = MathF.Pow(distance / minDistance, -falloffFactor) * scale;
                    break;
                case AudioFalloffModel.FalloffType.LinearDistance:
                    distance = Math.Clamp(distance, minDistance, maxDistance);
                    volume = 1 - falloffFactor * (distance - minDistance) / (maxDistance - minDistance);
                    break;
                default:
                    volume = 1.0f;
                    break;
            }
        }
        catch (Exception e) when (e is DivideByZeroException or ArgumentException)
        {
            volume = 1.0f;
        }
        volume = Math.Clamp(volume, 0.0f, 1.0f);
        return volume;
    }

    private void SpatializeVolume(float volume, Vector3 toTarget, out float leftVolume, out float rightVolume)
    {
        leftVolume = rightVolume = volume;
        var distance = toTarget.Length();
        if (volume == 0 || distance == 0)
        {
            return;
        }

        var lookAtVector = Vector3.Zero;
        unsafe
        {
            // https://github.com/NotNite/Linkpearl/blob/main/Linkpearl/Plugin.cs
            var renderingCamera = *FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera;
            lookAtVector = new Vector3(renderingCamera.ViewMatrix.M13, renderingCamera.ViewMatrix.M23, renderingCamera.ViewMatrix.M33);
        }

        if (lookAtVector.LengthSquared() == 0)
        {
            return;
        }

        var toTargetHorizontal = Vector3.Normalize(new Vector3(toTarget.X, 0, toTarget.Z));
        var cameraForwardHorizontal = Vector3.Normalize(new Vector3(lookAtVector.X, 0, lookAtVector.Z));
        var dot = Vector3.Dot(toTargetHorizontal, cameraForwardHorizontal);
        var cross = Vector3.Dot(Vector3.Cross(toTargetHorizontal, cameraForwardHorizontal), Vector3.UnitY);
        var cosine = -dot;
        var sine = cross;
        var angle = MathF.Acos(cosine);
        if (float.IsNaN(angle)) { angle = 0f; }
        if (sine < 0) { angle = -angle; }

        var minDistance = this.configuration.FalloffModel.MinimumDistance;
        float pan = 1;
        if (minDistance > 0 && distance < minDistance)
        {
            // Linear pan dropoff
            pan = distance / minDistance;
            // Arc pan dropoff
            //var d = distance / minDistance;
            //pan = 1 - MathF.Sqrt(1 - d * d);
        }
        pan = Math.Abs(pan * MathF.Sin(angle));
        if (angle > 0)
        {
            // Left of player
            rightVolume *= 1 - pan;
        }
        else
        {
            // Right of player
            leftVolume *= 1 - pan;
        }
    }

    //private void DebugStuff()
    //{
    //    // Debug stuff
    //    unsafe
    //    {
    //        var renderingCamera = *FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera;
    //        // renderingCamera.Rotation is always (0,0,0,1)
    //        // renderingCamera.LookAtVector doesn't seem accurate
    //        // renderingCamera.Vector_1 seems to only change with camera pitch (not useful)
    //        // renderingCamera.Position = renderingCamera.Object.Position
    //        // https://github.com/NotNite/Linkpearl/blob/main/Linkpearl/Plugin.cs
    //        var lookAtVector = new Vector3(renderingCamera.ViewMatrix.M13, renderingCamera.ViewMatrix.M23, renderingCamera.ViewMatrix.M33);
    //        //Matrix4x4.Decompose(renderingCamera.ViewMatrix, out var scale, out var rotation, out var position);
    //        this.logger.Debug("PlayerPosition {0}, CameraPosition {1}, LookAtVector {2}, CameraUp {3}",
    //            (this.clientState.LocalPlayer?.Position ?? Vector3.Zero).ToString("F2", null),
    //            renderingCamera.Position.ToString("F2", null),  // useful
    //            lookAtVector.ToString("F2", null),  // useful
    //            renderingCamera.Vector_1.ToString("F2", null)); // not useful

    //        var npcs = this.objectTable
    //            .Where(go => go.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc)
    //            .OfType<Dalamud.Game.ClientState.Objects.Types.ICharacter>();
    //        foreach (var npc in npcs)
    //        {
    //            if (npc.Name.TextValue == "Storm Squadron Sergeant")
    //            {
    //                var toTarget = npc.Position - this.clientState.LocalPlayer?.Position ?? Vector3.Zero;
    //                var distance = toTarget.Length();
    //                var volume = CalculateVolume(distance);

    //                var toTargetHorizontal = Vector3.Normalize(new Vector3(toTarget.X, 0, toTarget.Z));
    //                var cameraForwardHorizontal = Vector3.Normalize(new Vector3(lookAtVector.X, 0, lookAtVector.Z));
    //                var dot = Vector3.Dot(toTargetHorizontal, cameraForwardHorizontal);
    //                var cross = Vector3.Dot(Vector3.Cross(toTargetHorizontal, cameraForwardHorizontal), Vector3.UnitY);
    //                var sine = cross;
    //                var cosine = -dot;
    //                var angle = MathF.Acos(cosine);
    //                if (float.IsNaN(angle)) { angle = 0f; }
    //                if (sine < 0) { angle = -angle; }

    //                var left = volume;
    //                var right = volume;
    //                var minDistance = this.configuration.FalloffModel.MinimumDistance;
    //                float pan = 1;
    //                if (minDistance > 0 && distance < minDistance)
    //                {
    //                    // Linear pan dropoff
    //                    pan = distance / minDistance;
    //                    // Arc pan dropoff
    //                    //var d = distance / minDistance;
    //                    //pan = 1 - MathF.Sqrt(1 - d * d);
    //                }
    //                pan = Math.Abs(pan * MathF.Sin(angle));
    //                if (angle > 0)
    //                {
    //                    // Left of player
    //                    right *= 1 - pan;
    //                }
    //                else
    //                {
    //                    // Right of player
    //                    left *= 1 - pan;
    //                }

    //                this.logger.Debug("NPC {0}, position {1}, distance {2}, calcVolume {3}, angle {4}, left {5}, right {6}",
    //                    npc.Name,
    //                    npc.Position.ToString("F2", null),
    //                    distance.ToString("F2", null),
    //                    volume.ToString("F2", null),
    //                    double.RadiansToDegrees(angle).ToString("F2", null),
    //                    left.ToString("F2", null),
    //                    right.ToString("F2", null));


    //                left *= this.configuration.MasterVolume;
    //                right *= this.configuration.MasterVolume;
    //                this.audioDeviceController.SetChannelVolume("testPeer1", left, right);
    //            }
    //        }
    //    }
    //}
}
