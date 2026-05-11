using System.Text.RegularExpressions;

namespace ProximityVoiceChat;

public static class Utils
{
    /// <summary>
    /// Requires an input format of Firstname Lastname@Worldname. Returns the input string if format is wrong.
    /// </summary>
    public static string ConvertToInitialsName(string playerName)
    {
        var regex = @"(\w)\w*\s(\w)\w*(@\w+)";

        var match = Regex.Match(playerName, regex);
        if (match != null)
        {
            var groups = match.Groups;
            if (groups != null && groups.Count >= 4)
            {
                // S. N.@World
                return $"{groups[1].Value}. {groups[2].Value}.{groups[3].Value}";
            }
        }
        return playerName;
    }
}
