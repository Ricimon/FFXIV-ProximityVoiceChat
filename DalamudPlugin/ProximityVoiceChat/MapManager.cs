using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using ProximityVoiceChat.Extensions;
using ProximityVoiceChat.Log;
using System;
using System.Text;

namespace ProximityVoiceChat;

public sealed class MapManager : IDisposable
{
    public ushort CurrentTerritoryId => this.clientState.TerritoryType;
    public uint CurrentMapId => this.clientState.MapId;

    public event System.Action? OnMapChanged;

    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly ILogger logger;

    public MapManager(IClientState clientState, IDataManager dataManager, ILogger logger)
    {
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.logger = logger;

        this.clientState.TerritoryChanged += OnTerritoryChanged;
        OnTerritoryChanged(this.clientState.TerritoryType);
    }

    public void Dispose()
    {
        this.clientState.TerritoryChanged -= OnTerritoryChanged;
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

        //var currentCfc = this.dataManager.GetExcelSheet<ContentFinderCondition>().GetRow(GameMain.Instance()->CurrentContentFinderConditionId);
        //return currentCfc.RowId is 0;
    }

    public unsafe string GetCurrentMapPublicRoomName()
    {
        var s = new StringBuilder("public");
        if (InSharedWorldMap())
        {
            s.Append('_');
            if (this.clientState.LocalPlayer != null)
            {
                s.Append(this.clientState.LocalPlayer.CurrentWorld.Value.Name.ToString());
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
                    //s.Append("_r"); s.Append(housingManager->GetCurrentRoom());
                    //s.Append("_p"); s.Append(housingManager->GetCurrentPlot());
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
