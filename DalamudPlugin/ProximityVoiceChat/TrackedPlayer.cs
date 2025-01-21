namespace ProximityVoiceChat;

public class TrackedPlayer
{
    public float Distance { get; set; } = float.NaN;
    public float Volume { get; set; } = 1.0f;
    public int? LastTickFoundAlive { get; set; } = null;
}
