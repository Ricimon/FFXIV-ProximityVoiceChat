using System;
using System.Collections.Generic;
using System.Linq;
using ProximityVoiceChat.Log;
using ProximityVoiceChat.SDL2;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.SDL2;

namespace ProximityVoiceChat;

public class AudioDeviceController : IDisposable
{
    public bool IsAudioRecordingSourceActive => PlayingBackMicAudio || AudioRecordingIsRequested;
    public bool IsAudioPlaybackSourceActive => PlayingBackMicAudio || AudioPlaybackIsRequested;

    public bool PlayingBackMicAudio
    {
        get => this.playingBackMicAudio;
        set
        {
            this.playingBackMicAudio = value;
            UpdateSourceStates();
        }
    }
    private bool playingBackMicAudio;

    public bool AudioRecordingIsRequested
    {
        get => this.audioRecordingIsRequested;
        set
        {
            this.audioRecordingIsRequested = value;
            UpdateSourceStates();
        }
    }
    private bool audioRecordingIsRequested;

    public bool AudioPlaybackIsRequested
    {
        get => this.audioPlaybackIsRequested;
        set
        {
            this.audioPlaybackIsRequested = value;
            UpdateSourceStates();
        }
    }
    private bool audioPlaybackIsRequested;

    public int AudioRecordingDeviceIndex
    {
        get => this.audioRecordingDeviceIndex;
        set
        {
            if (this.audioRecordingDeviceIndex != value)
            {
                this.audioRecordingDeviceIndex = value;
                this.configuration.SelectedAudioInputDeviceIndex = value;
                this.configuration.Save();

                DisposeAudioRecordingSource();
                if (IsAudioRecordingSourceActive)
                {
                    InitializeAudioRecordingSource();
                }
            }
        }
    }
    private int audioRecordingDeviceIndex;

    public int AudioPlaybackDeviceIndex
    {
        get => this.audioPlaybackDeviceIndex;
        set
        {
            if (this.audioPlaybackDeviceIndex != value)
            {
                this.audioPlaybackDeviceIndex = value;
                this.configuration.SelectedAudioOutputDeviceIndex = value;
                this.configuration.Save();

                this.audioPlaybackSource?.CloseAudioSink();
                this.audioPlaybackSource = null;
                if (IsAudioPlaybackSourceActive)
                {
                    InitializeAudioPlaybackSource();
                }
            }
        }
    }
    private int audioPlaybackDeviceIndex;

    public event EncodedSampleDelegate? OnAudioRecordingSourceEncodedSample;

    private SDL2.SDL2AudioSource? audioRecordingSource;
    private SDL2.SDL2AudioEndPoint? audioPlaybackSource;

    // best sounding format found through testing
    private AudioFormat AudioFormat => audioEncoder.SupportedFormats[2];

    private readonly IAudioEncoder audioEncoder = new AudioEncoder();
    private readonly Configuration configuration;
    private readonly ILogger logger;

    public AudioDeviceController(Configuration configuration, ILogger logger)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.audioRecordingDeviceIndex = configuration.SelectedAudioInputDeviceIndex;
        this.audioPlaybackDeviceIndex = configuration.SelectedAudioOutputDeviceIndex;

        SDL2Helper.InitSDL();
    }

    public void Dispose()
    {
        DisposeAudioRecordingSource();
        this.audioPlaybackSource?.CloseAudioSink();
        SDL2Helper.QuitSDL();
    }

    public List<string> GetAudioRecordingDevices()
    {
        return SDL2Helper.GetAudioRecordingDevices();
    }

    public List<string> GetAudioPlaybackDevices()
    {
        return SDL2Helper.GetAudioPlaybackDevices();
    }

    private void InitializeAudioRecordingSource()
    {
        var deviceName = this.AudioRecordingDeviceIndex >= 0 ? SDL2Helper.GetAudioRecordingDevice(this.AudioRecordingDeviceIndex) : null;
        this.audioRecordingSource = new SDL2.SDL2AudioSource(deviceName, this.audioEncoder, this.logger);

        this.audioRecordingSource.OnAudioSourceEncodedSample += this.OnAudioSourceEncodedSample;
        this.audioRecordingSource.OnAudioSourceError += this.OnAudioSourceError;

        // This starts the audio
        this.audioRecordingSource.SetAudioSourceFormat(AudioFormat);
    }

    private void DisposeAudioRecordingSource()
    {
        if (this.audioRecordingSource != null)
        {
            this.audioRecordingSource.OnAudioSourceEncodedSample -= this.OnAudioSourceEncodedSample;
            this.audioRecordingSource.OnAudioSourceError -= this.OnAudioSourceError;
            this.audioRecordingSource.CloseAudio();
            this.audioRecordingSource = null;
        }
    }

    private void InitializeAudioPlaybackSource()
    {
        var deviceName = this.AudioPlaybackDeviceIndex >= 0 ? SDL2Helper.GetAudioPlaybackDevice(this.AudioPlaybackDeviceIndex) : null;
        this.audioPlaybackSource = new SDL2.SDL2AudioEndPoint(deviceName, this.audioEncoder, this.logger);

        // This starts the audio
        this.audioPlaybackSource.SetAudioSinkFormat(AudioFormat);
    }

    private void OnAudioSourceEncodedSample(uint durationRtpUnits, byte[] sample)
    {
        this.logger.Trace("Audio encoded sample received. DurationRtpUnits {0}, Sample {1}", durationRtpUnits, sample);
        if (this.audioPlaybackSource != null && this.PlayingBackMicAudio)
        {
            var pcmSample = this.audioEncoder.DecodeAudio(sample, AudioFormat);
            var pcmBytes = pcmSample.SelectMany(x => BitConverter.GetBytes(x)).ToArray();
            this.audioPlaybackSource.GotAudioSample(pcmBytes);
        }
        this.OnAudioRecordingSourceEncodedSample?.Invoke(durationRtpUnits, sample);
    }

    private void OnAudioSourceError(string message)
    {
        this.logger.Error(message);
    }

    private void UpdateSourceStates()
    {
        if (this.IsAudioRecordingSourceActive)
        {
            if (this.audioRecordingSource == null)
            {
                InitializeAudioRecordingSource();
            }
            else
            {
                this.audioRecordingSource.ResumeAudio();
            }
        }
        else
        {
            this.audioRecordingSource?.PauseAudio();
        }

        if (this.IsAudioPlaybackSourceActive)
        {
            if (this.audioPlaybackSource == null)
            {
                InitializeAudioPlaybackSource();
            }
            else
            {
                this.audioPlaybackSource.ResumeAudioSink();
            }
        }
        else
        {
            this.audioPlaybackSource?.PauseAudioSink();
        }
    }
}
