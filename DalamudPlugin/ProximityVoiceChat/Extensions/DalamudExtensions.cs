﻿using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;

namespace ProximityVoiceChat.Extensions;

public static class DalamudExtensions
{
    public static string? GetPlayerFullName(this IPlayerCharacter playerCharacter)
    {
        string playerName = playerCharacter.Name.TextValue;
        var homeWorld = playerCharacter.HomeWorld;
        if (homeWorld.IsValid)
        {
            playerName += $"@{homeWorld.Value.Name.ExtractText()}";
        }

        return playerName;
    }

    public static string? GetLocalPlayerFullName(this IClientState clientState)
    {
        var localPlayer = clientState.LocalPlayer;
        if (localPlayer == null)
        {
            return null;
        }
        return GetPlayerFullName(localPlayer);
    }
}
