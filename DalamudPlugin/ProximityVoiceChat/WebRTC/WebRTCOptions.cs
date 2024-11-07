namespace ProximityVoiceChat.WebRTC;

public class WebRTCOptions
{
    public bool EnableDataChannel;
    public WebRTCDataChannelHandler.IFactory? DataChannelHandlerFactory;
    public required string TurnServerUrl;
    public string? TurnUsername;
    public string? TurnPassword;
}
