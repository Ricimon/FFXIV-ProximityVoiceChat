// Taken from https://github.com/MidoriKami/KamiLib/blob/master/Classes/TerritoryIntendedUseEnum.cs
using System.ComponentModel;

namespace ProximityVoiceChat.Extensions;

public enum TerritoryIntendedUseEnum
{
    [Description("Town")]
    Town = 0,

    [Description("Open World")]
    OpenWorld = 1,

    [Description("Inn")]
    Inn = 2,

    [Description("Dungeon")]
    Dungeon = 3,

    [Description("Jail Area")]
    JailArea = 5,

    [Description("Opening Area")]
    OpeningArea = 6,

    [Description("Pre Trial Dungeon")]
    BeforeTrialDung = 7,

    [Description("Alliance Raid")]
    AllianceRaid = 8,

    [Description("Open World Instance Battle")]
    OpenWorldInstanceBattle = 9,

    [Description("Trial")]
    Trial = 10,

    [Description("Raid Public Area")]
    RaidPublicArea = 12,

    [Description("Housing Area")]
    HousingArea = 13,

    [Description("Housing Private Area")]
    HousingPrivateArea = 14,

    [Description("MSQ Private Area")]
    MainStoryQuestPrivateArea = 15,

    [Description("Raids")]
    Raids = 16,

    [Description("Raid Fights")]
    RaidFights = 17,

    [Description("Chocobo Square")]
    ChocoboSquare = 19,

    [Description("Chocobo Tutorial")]
    ChocoboTutorial = 21,

    [Description("Wedding")]
    Wedding = 22,

    [Description("Gold Saucer")]
    GoldSaucer = 23,

    [Description("Diadem v1")]
    DiademV1 = 26,

    [Description("Beginner Tutorial")]
    BeginnerTutorial = 27,

    [Description("PvP The Feast")]
    PvPTheFeast = 28,

    [Description("MSQ Event Area")]
    MainStoryQuestEventArea = 29,

    [Description("Free Company Garrison")]
    FreeCompanyGarrison = 30,

    [Description("Palace of the Dead")]
    PalaceOfTheDead = 31,

    [Description("Treasure Map Instance")]
    TreasureMapInstance = 33,

    [Description("Event Trial")]
    EventTrial = 36,

    [Description("The Feast Area")]
    TheFeastArea = 37,

    [Description("Diadem v2")]
    DiademV2 = 38,

    [Description("Private Event Area")]
    PrivateEventArea = 40,

    [Description("Eureka")]
    Eureka = 41,

    [Description("The Feast Crystal Tower")]
    TheFeastCrystalTower = 42,

    [Description("Leap of Faith")]
    LeapOfFaith = 44,

    [Description("Masked Carnivale")]
    MaskedCarnival = 45,

    [Description("Ocean Fishing")]
    OceanFishing = 46,

    [Description("Diadem v3")]
    DiademV3 = 47,

    [Description("Bozja")]
    Bozja = 48,
}
