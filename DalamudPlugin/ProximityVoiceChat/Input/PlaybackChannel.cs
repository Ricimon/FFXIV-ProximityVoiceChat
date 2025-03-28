using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using WebRtcVadSharp;

namespace ProximityVoiceChat.Input;

public class PlaybackChannel : IDisposable
{
    public required MonoToStereoSampleProvider MonoToStereoSampleProvider { get; set; }
    public required BufferedWaveProvider BufferedWaveProvider { get; set; }
    public WaveInEventArgs? LastSampleAdded { get; set; }
    public int LastSampleAddedTimestampMs { get; set; }
    public int BufferClearedEventTimestampMs { get; set; }
    public WebRtcVad VoiceActivityDetector { get; set; } = new()
    {
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is48kHz,
    };

    public void Dispose()
    {
        this.VoiceActivityDetector.Dispose();
        GC.SuppressFinalize(this);
    }
}
