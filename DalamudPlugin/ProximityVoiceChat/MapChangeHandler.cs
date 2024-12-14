using Dalamud.Plugin.Services;
using ProximityVoiceChat.Log;
using System;

namespace ProximityVoiceChat;

public class MapChangeHandler : IDisposable
{
    private readonly IClientState clientState;
    private readonly ILogger logger;

    public MapChangeHandler(IClientState clientState, ILogger logger)
    {
        this.clientState = clientState;
        this.logger = logger;

        this.clientState.TerritoryChanged += OnTerritoryChanged;
        OnTerritoryChanged(this.clientState.TerritoryType);
    }

    public void Dispose()
    {
        this.clientState.TerritoryChanged -= OnTerritoryChanged;
        GC.SuppressFinalize(this);
    }

    private void OnTerritoryChanged(ushort obj)
    {
        this.logger.Debug($"Territory changed to {obj}, map ID is {this.clientState.MapId}");
    }
}
