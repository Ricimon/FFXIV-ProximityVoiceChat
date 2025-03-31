using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public static IEnumerable<IPlayerCharacter> GetPlayers(this IObjectTable objectTable)
    {
        return objectTable.Where(go => go.ObjectKind == ObjectKind.Player).OfType<IPlayerCharacter>();
    }

    public static string GetResourcePath(this IDalamudPluginInterface pluginInterface, string fileName)
    {
        var resourcesDir = Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, "Resources");
        return Path.Combine(resourcesDir, fileName);
    }
}
