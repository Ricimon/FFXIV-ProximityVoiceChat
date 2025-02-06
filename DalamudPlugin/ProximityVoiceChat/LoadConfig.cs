namespace ProximityVoiceChat;

public class LoadConfig
{
#pragma warning disable IDE1006 // Naming Styles
    public string signalingServerUrl { get; set; } = string.Empty;
    public string signalingServerToken { get; set; } = string.Empty;
    public string? turnServerUrlOverride { get; set; }
    public string? turnServerUsernameOverride { get; set; }
    public string? turnServerPasswordOverride { get; set; }
#pragma warning restore IDE1006 // Naming Styles
}
