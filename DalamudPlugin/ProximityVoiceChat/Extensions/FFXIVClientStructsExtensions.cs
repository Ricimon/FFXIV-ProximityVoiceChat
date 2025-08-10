using FFXIVClientStructs.FFXIV.Client.Game;

namespace ProximityVoiceChat.Extensions;

public static class FFXIVClientStructsExtensions
{
    public static bool IsValid(this HouseId houseId)
    {
        return houseId != ulong.MaxValue;
    }
}
