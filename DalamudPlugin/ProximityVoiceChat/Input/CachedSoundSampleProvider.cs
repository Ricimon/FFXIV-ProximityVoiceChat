using NAudio.Wave;
using System;

namespace ProximityVoiceChat.Input;

//https://markheath.net/post/fire-and-forget-audio-playback-with 
public class CachedSoundSampleProvider(CachedSound cachedSound) : ISampleProvider
{
    private readonly CachedSound cachedSound = cachedSound;
    private long position;

    public int Read(float[] buffer, int offset, int count)
    {
        var availableSamples = cachedSound.AudioData.Length - position;
        var samplesToCopy = Math.Min(availableSamples, count);
        Array.Copy(cachedSound.AudioData, position, buffer, offset, samplesToCopy);
        position += samplesToCopy;
        return (int)samplesToCopy;
    }

    public WaveFormat WaveFormat => cachedSound.WaveFormat;
}
