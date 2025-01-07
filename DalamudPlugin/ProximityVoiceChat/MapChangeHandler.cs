using Dalamud.Plugin.Services;
using ProximityVoiceChat.Log;
using System;
using System.Threading.Tasks;

namespace ProximityVoiceChat;

public class MapChangeHandler : IDisposable
{
    public ushort CurrentTerritoryId => this.clientState.TerritoryType;
    public uint CurrentMapId => this.clientState.MapId;

    /// <summary>
    /// Fires when the territory changes, and carries the new public room name as the argument.
    /// </summary>
    public event Action<string>? OnMapChanged;

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

    public string GetCurrentMapPublicRoomName()
    {
        return string.Format("public{0}_{1}", CurrentTerritoryId, CurrentMapId);
    }

    private void OnTerritoryChanged(ushort obj)
    {
        if (OnMapChanged == null)
        {
            return;
        }

        Task.Run(async () =>
        {
            // In some housing districts, the mapId is different after the OnTerritoryChanged event
            await Task.Delay(500);
            OnMapChanged?.Invoke(GetCurrentMapPublicRoomName());
        });
    }
}
