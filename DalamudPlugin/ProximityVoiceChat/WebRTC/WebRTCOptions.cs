namespace ProximityVoiceChat.WebRTC;

public class WebRTCOptions
{
    public bool EnableDataChannel;
    public WebRTCDataChannelHandler.IFactory? DataChannelHandlerFactory;
    public string? TurnServerUrlOverride;
    public string? TurnServerUsernameOverride;
    public string? TurnServerPasswordOverride;
}
