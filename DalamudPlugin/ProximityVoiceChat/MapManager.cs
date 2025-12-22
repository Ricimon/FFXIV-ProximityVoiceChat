using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Log;
using System;
using System.Text;

namespace ProximityVoiceChat;

public sealed class MapManager : IDisposable
{
    public ushort CurrentTerritoryId => this.dalamud.ClientState.TerritoryType;
    public uint CurrentMapId => this.dalamud.ClientState.MapId;

    public event Action? OnMapChanged;

    private readonly DalamudServices dalamud;
    private readonly ILogger logger;

    public MapManager(DalamudServices dalamud, ILogger logger)
    {
        this.dalamud = dalamud;
        this.logger = logger;

        this.dalamud.ClientState.TerritoryChanged += OnTerritoryChanged;
        OnTerritoryChanged(this.dalamud.ClientState.TerritoryType);
    }

    public void Dispose()
    {
        this.dalamud.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    public unsafe bool InSharedWorldMap()
    {
        var territoryIntendedUse = (TerritoryIntendedUseEnum)GameMain.Instance()->CurrentTerritoryIntendedUseId;

        switch (territoryIntendedUse)
        {
            case TerritoryIntendedUseEnum.Town:
            case TerritoryIntendedUseEnum.OpenWorld:
            case TerritoryIntendedUseEnum.RaidPublicArea:
            case TerritoryIntendedUseEnum.HousingArea:
            case TerritoryIntendedUseEnum.GoldSaucer:
                return true;
            case TerritoryIntendedUseEnum.HousingPrivateArea:
                return HousingManager.Instance()->GetCurrentIndoorHouseId().IsValid();
            case TerritoryIntendedUseEnum.Inn:
            case TerritoryIntendedUseEnum.Dungeon:
            case TerritoryIntendedUseEnum.JailArea:
            case TerritoryIntendedUseEnum.OpeningArea:
            case TerritoryIntendedUseEnum.BeforeTrialDung:
            case TerritoryIntendedUseEnum.AllianceRaid:
            case TerritoryIntendedUseEnum.OpenWorldInstanceBattle:
            case TerritoryIntendedUseEnum.Trial:
            case TerritoryIntendedUseEnum.MainStoryQuestPrivateArea:
            case TerritoryIntendedUseEnum.Raids: // need to check
            case TerritoryIntendedUseEnum.RaidFights:
            case TerritoryIntendedUseEnum.ChocoboSquare: // need to check
            case TerritoryIntendedUseEnum.ChocoboTutorial:
            case TerritoryIntendedUseEnum.Wedding:
            case TerritoryIntendedUseEnum.DiademV1:
            case TerritoryIntendedUseEnum.BeginnerTutorial:
            case TerritoryIntendedUseEnum.PvPTheFeast: // need to check
            case TerritoryIntendedUseEnum.MainStoryQuestEventArea: // need to check
            case TerritoryIntendedUseEnum.FreeCompanyGarrison:
            case TerritoryIntendedUseEnum.PalaceOfTheDead:
            case TerritoryIntendedUseEnum.TreasureMapInstance:
            case TerritoryIntendedUseEnum.EventTrial: // need to check
            case TerritoryIntendedUseEnum.TheFeastArea: // need to check
            case TerritoryIntendedUseEnum.DiademV2:
            case TerritoryIntendedUseEnum.PrivateEventArea: // need to check
            case TerritoryIntendedUseEnum.Eureka:
            case TerritoryIntendedUseEnum.TheFeastCrystalTower:
            case TerritoryIntendedUseEnum.LeapOfFaith:
            case TerritoryIntendedUseEnum.MaskedCarnival:
            case TerritoryIntendedUseEnum.OceanFishing:
            case TerritoryIntendedUseEnum.DiademV3:
            case TerritoryIntendedUseEnum.Bozja:
            default:
                return false;
        }
    }

    public unsafe string GetCurrentMapPublicRoomName()
    {
        var s = new StringBuilder("public");
        if (InSharedWorldMap())
        {
            s.Append('_');
            if (this.dalamud.PlayerState.IsLoaded)
            {
                s.Append(this.dalamud.PlayerState.CurrentWorld.Value.Name.ToString());
            }
            else
            {
                s.Append("Unknown");
            }
        }
        else
        {
            s.Append("_Instance");
        }
        s.Append("_t"); s.Append(CurrentTerritoryId);
        s.Append("_m"); s.Append(CurrentMapId);

        var instance = UIState.Instance()->PublicInstance;
        if (instance.IsInstancedArea())
        {
            s.Append("_i"); s.Append(instance.InstanceId);
        }

        var territoryIntendedUse = (TerritoryIntendedUseEnum)GameMain.Instance()->CurrentTerritoryIntendedUseId;
        if (territoryIntendedUse == TerritoryIntendedUseEnum.HousingArea ||
            territoryIntendedUse == TerritoryIntendedUseEnum.HousingPrivateArea)
        {
            var housingManager = HousingManager.Instance();
            var houseId = housingManager->GetCurrentIndoorHouseId();
            if (houseId.IsValid())
            {
                s.Append("_h"); s.Append(houseId);
            }
            else
            {
                var ward = housingManager->GetCurrentWard();
                if (ward != -1)
                {
                    s.Append("_w"); s.Append(ward);
                    var division = housingManager->GetCurrentDivision();
                    if (division != 0)
                    {
                        s.Append("_d"); s.Append(division);
                    }
                }
            }
        }
        return s.ToString();
    }

    private void OnTerritoryChanged(ushort obj)
    {
        OnMapChanged?.Invoke();
    }
}
