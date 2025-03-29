using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProximityVoiceChat.Input;

public interface IAudioDeviceController
{
    public bool IsAudioRecordingSourceActive { get; }
    public bool IsAudioPlaybackSourceActive { get; }

    public bool MuteMic { get; set; }
    public bool Deafen { get; set; }

    public bool PlayingBackMicAudio { get; set; }

    public bool AudioRecordingIsRequested { get; set; }
    public bool AudioPlaybackIsRequested { get; set; }

    public int AudioRecordingDeviceIndex { get; set; }
    public int AudioPlaybackDeviceIndex { get; set; }

    public event EventHandler<WaveInEventArgs>? OnAudioRecordingSourceDataAvailable;
    public bool RecordingDataHasActivity { get; }

    IEnumerable<string> GetAudioRecordingDevices();
    IEnumerable<string> GetAudioPlaybackDevices();

    void CreateAudioPlaybackChannel(string channelName);
    void RemoveAudioPlaybackChannel(string channelName);

    void AddPlaybackSample(string channelName, WaveInEventArgs sample);

    void ResetAllChannelsVolume(float volume);
    void SetChannelVolume(string channelName, float volume);

    bool ChannelHasActivity(string channelName);

    Task PlaySfx(CachedSound sound);
}
