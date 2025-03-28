using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace ProximityVoiceChat;

public class Spatializer(
    IClientState clientState,
    Configuration configuration)
{
    private readonly IClientState clientState = clientState;
    private readonly Configuration configuration = configuration;

    public void CalculateSpatialValues(
        IPlayerCharacter otherPlayer,
        TrackedPlayer? otherTrackedPlayer,
        int thisTick,
        out float leftVolume,
        out float rightVolume,
        out float distance)
    {
        if (this.clientState.LocalPlayer != null)
        {
            distance = Vector3.Distance(this.clientState.LocalPlayer.Position, otherPlayer.Position);
        }
        else
        {
            distance = 0;
        }
        var deathMute = this.configuration.MuteDeadPlayers;

        if (otherTrackedPlayer != null)
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

        leftVolume = volume;
        rightVolume = volume;
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

}
