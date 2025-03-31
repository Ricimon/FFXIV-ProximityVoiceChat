using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using ProximityVoiceChat.Log;

namespace ProximityVoiceChat;

public class Plugin(
    IDalamudPluginInterface pluginInterface,
    IEnumerable<IDalamudHook> dalamudHooks,
    Spatializer spatializer,
    ILogger logger)
{
    private IDalamudPluginInterface PluginInterface { get; init; } = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
    private IEnumerable<IDalamudHook> DalamudHooks { get; init; } = dalamudHooks ?? throw new ArgumentNullException(nameof(dalamudHooks));
    private Spatializer Spatializer { get; init; } = spatializer;
    private ILogger Logger { get; init; } = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Initialize()
    {
        foreach (var dalamudHook in this.DalamudHooks)
        {
            dalamudHook.HookToDalamud();
        }

        this.Spatializer.StartUpdateLoop();

        Logger.Info("ProximityVoiceChat initialized");
    }
}
