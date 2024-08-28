using NAudio.Wave;
using System.Threading;

namespace ProximityVoiceChat;

public class SoundEngine
{
    public SoundEngine()
    {
        using (new WaveOutEvent()) { }
    }

    public WaveOutEvent PlaySound(string path)
    {
        // Forcibly start a new thread to avoid locking up the game
        var outputDevice = new WaveOutEvent();
        new Thread(() =>
        {
            using (var audioFile = new Mp3FileReader(path))
            {
                outputDevice.Init(audioFile);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(500);
                }
                outputDevice.Dispose();
            }
        }).Start();
        return outputDevice;
    }
}
