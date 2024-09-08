namespace ProximityVoiceChat;

public class LoadConfig
{
#pragma warning disable IDE1006 // Naming Styles
    public string token { get; set; } = string.Empty;
    public string? serverUrlOverride { get; set; }
    public string? turnUsername { get; set; }
    public string? turnPassword { get; set; }
#pragma warning restore IDE1006 // Naming Styles
}
