using NAudio.Wave;
using System;
using System.Collections.Generic;
using ProximityVoiceChat.Log;
using WindowsInput.Events;
using System.Threading.Tasks;

namespace ProximityVoiceChat.Input;

public class PushToTalkController : IAudioDeviceController
{
    bool IAudioDeviceController.IsAudioRecordingSourceActive => this.baseAudioDeviceController.IsAudioRecordingSourceActive;
    bool IAudioDeviceController.IsAudioPlaybackSourceActive => this.baseAudioDeviceController.IsAudioPlaybackSourceActive;

    bool IAudioDeviceController.MuteMic
    {
        get => this.baseAudioDeviceController.MuteMic;
        set => this.baseAudioDeviceController.MuteMic = value;
    }
    bool IAudioDeviceController.Deafen
    {
        get => this.baseAudioDeviceController.Deafen;
        set => this.baseAudioDeviceController.Deafen = value;
    }

    bool IAudioDeviceController.PlayingBackMicAudio
    {
        get => this.baseAudioDeviceController.PlayingBackMicAudio;
        set => this.baseAudioDeviceController.PlayingBackMicAudio = value;
    }

    // This is the single variable that this controller manages
    bool IAudioDeviceController.AudioRecordingIsRequested
    {
        get => this.audioRecordingIsExternallyRequested;
        set
        {
            this.audioRecordingIsExternallyRequested = value;
            UpdateBaseAudioRecordingIsRequested();
        }
    }
    bool IAudioDeviceController.AudioPlaybackIsRequested
    {
        get => this.baseAudioDeviceController.AudioPlaybackIsRequested;
        set => this.baseAudioDeviceController.AudioPlaybackIsRequested = value;
    }

    int IAudioDeviceController.AudioRecordingDeviceIndex
    {
        get => this.baseAudioDeviceController.AudioRecordingDeviceIndex;
        set => this.baseAudioDeviceController.AudioRecordingDeviceIndex = value;
    }
    int IAudioDeviceController.AudioPlaybackDeviceIndex
    {
        get => this.baseAudioDeviceController.AudioPlaybackDeviceIndex;
        set => this.baseAudioDeviceController.AudioPlaybackDeviceIndex = value;
    }

    event EventHandler<WaveInEventArgs>? IAudioDeviceController.OnAudioRecordingSourceDataAvailable
    {
        add => this.baseAudioDeviceController.OnAudioRecordingSourceDataAvailable += value;
        remove => this.baseAudioDeviceController.OnAudioRecordingSourceDataAvailable -= value;
    }
    bool IAudioDeviceController.RecordingDataHasActivity => this.baseAudioDeviceController.RecordingDataHasActivity;

    public bool PushToTalkKeyDown { get; private set; }

    private readonly IAudioDeviceController baseAudioDeviceController;
    private readonly Configuration configuration;
    private readonly InputEventSource inputEventSource;
    private readonly ILogger logger;

    private bool audioRecordingIsExternallyRequested;
    private bool listenerSubscribed;

    public PushToTalkController(
        IAudioDeviceController baseAudioDeviceController,
        Configuration configuration,
        InputEventSource inputEventSource,
        ILogger logger)
    {
        this.baseAudioDeviceController = baseAudioDeviceController ?? throw new ArgumentNullException(nameof(baseAudioDeviceController));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.inputEventSource = inputEventSource ?? throw new ArgumentNullException(nameof(inputEventSource));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        UpdateListeners();
    }

    void IAudioDeviceController.AddPlaybackSample(string channelName, WaveInEventArgs sample)
    {
        this.baseAudioDeviceController.AddPlaybackSample(channelName, sample);
    }

    bool IAudioDeviceController.ChannelHasActivity(string channelName)
    {
        return this.baseAudioDeviceController.ChannelHasActivity(channelName);
    }

    void IAudioDeviceController.CreateAudioPlaybackChannel(string channelName)
    {
        this.baseAudioDeviceController.CreateAudioPlaybackChannel(channelName);
    }

    IEnumerable<string> IAudioDeviceController.GetAudioPlaybackDevices()
    {
        return this.baseAudioDeviceController.GetAudioPlaybackDevices();
    }

    IEnumerable<string> IAudioDeviceController.GetAudioRecordingDevices()
    {
        return this.baseAudioDeviceController.GetAudioRecordingDevices();
    }

    void IAudioDeviceController.RemoveAudioPlaybackChannel(string channelName)
    {
        this.baseAudioDeviceController.RemoveAudioPlaybackChannel(channelName);
    }

    void IAudioDeviceController.ResetAllChannelsVolume(float volume)
    {
        this.baseAudioDeviceController.ResetAllChannelsVolume(volume);
    }

    void IAudioDeviceController.SetChannelVolume(string channelName, float volume)
    {
        this.baseAudioDeviceController.SetChannelVolume(channelName, volume);
    }

    Task IAudioDeviceController.PlaySfx(CachedSound sound)
    {
        return this.baseAudioDeviceController.PlaySfx(sound);
    }

    public void UpdateListeners()
    {
        if (this.configuration.PushToTalk && !this.listenerSubscribed)
        {
            this.logger.Debug("Push to talk enabled. Subscribing listeners.");
            this.inputEventSource.SubscribeToKeyDown(OnInputKeyDown);
            this.inputEventSource.SubscribeToKeyUp(OnInputKeyUp);
            this.listenerSubscribed = true;
        }
        else if (!this.configuration.PushToTalk && this.listenerSubscribed)
        {
            this.logger.Debug("Push to talk disabled. Unsubscribing listeners.");
            this.inputEventSource.UnsubscribeToKeyDown(OnInputKeyDown);
            this.inputEventSource.UnsubscribeToKeyUp(OnInputKeyUp);
            this.PushToTalkKeyDown = false;
            this.listenerSubscribed = false;
        }
        UpdateBaseAudioRecordingIsRequested();
    }

    private void OnInputKeyDown(KeyDown k)
    {
        if (k.Key == this.configuration.PushToTalkKeybind)
        {
            this.PushToTalkKeyDown = true;
            if (this.audioRecordingIsExternallyRequested)
            {
                UpdateBaseAudioRecordingIsRequested();
            }
        }
    }

    private void OnInputKeyUp(KeyUp k)
    {
        if (k.Key == this.configuration.PushToTalkKeybind)
        {
            // TODO: Add release delay
            this.PushToTalkKeyDown = false;
            if (this.audioRecordingIsExternallyRequested)
            {
                UpdateBaseAudioRecordingIsRequested();
            }
        }
    }

    private void UpdateBaseAudioRecordingIsRequested()
    {
        if (this.configuration.PushToTalk)
        {
            this.baseAudioDeviceController.AudioRecordingIsRequested =
                this.audioRecordingIsExternallyRequested && this.PushToTalkKeyDown;
        }
        else
        {
            this.baseAudioDeviceController.AudioRecordingIsRequested =
                this.audioRecordingIsExternallyRequested;
        }
    }
}
