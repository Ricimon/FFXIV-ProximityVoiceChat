namespace ProximityVoiceChat.WebRTC;

public class WebRTCOptions
{
    public bool EnableDataChannel;
    public WebRTCDataChannelHandler.IFactory? DataChannelHandlerFactory;
}
