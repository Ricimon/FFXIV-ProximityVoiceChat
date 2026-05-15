using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using WebRtcVadSharp;

namespace ProximityVoiceChat.Input;

public sealed class PlaybackChannel : IDisposable
{
    public required MonoToStereoSampleProvider MonoToStereoSampleProvider { get; set; }
    public required BufferedWaveProvider BufferedWaveProvider { get; set; }
    public WaveInEventArgs? LastSampleAdded { get; set; }
    public int LastSampleAddedTimestampMs { get; set; }
    public WebRtcVad VoiceActivityDetector { get; set; } = new()
    {
        FrameLength = FrameLength.Is20ms,
        SampleRate = SampleRate.Is48kHz,
    };

    private int highestSampleTimestampDeltaMs;
    private int highestSampleTimestampDeltaMsRecordTimestamp;

    public void Dispose()
    {
        this.VoiceActivityDetector.Dispose();
    }

    public int GetHighestSampleTimestampDeltaMs(int nowMs)
    {
        // Over the course of 60 seconds, slowly reduce the highest sample timestamp delta to remove outlier ping spikes
        var decayDurationMs = 60000;
        var msSinceRecordTimestamp = nowMs - highestSampleTimestampDeltaMsRecordTimestamp;
        var decay = highestSampleTimestampDeltaMs * msSinceRecordTimestamp / decayDurationMs;
        return highestSampleTimestampDeltaMs - decay;
    }

    public void SetHighestSampleTimestampDeltaMs(int value, int nowMs)
    {
        highestSampleTimestampDeltaMs = value;
        highestSampleTimestampDeltaMsRecordTimestamp = nowMs;
    }
}
