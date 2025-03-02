using NAudio.Wave;
using System.Collections.Generic;
using System.Linq;

namespace ProximityVoiceChat.Input;

// https://markheath.net/post/fire-and-forget-audio-playback-with
public class CachedSound
{
    public float[] AudioData { get; private set; }
    public WaveFormat WaveFormat { get; private set; }
    
    public CachedSound(string audioFileName)
    {
        using (var reader = new AudioFileReader(audioFileName))
        {
            WaveFormat = reader.WaveFormat;
            var wholeFile = new List<float>((int)(reader.Length / 4));
            var readBuffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
            int samplesRead;
            while ((samplesRead = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                wholeFile.AddRange(readBuffer.Take(samplesRead));
            }
            AudioData = wholeFile.ToArray();
        }
    }
}
