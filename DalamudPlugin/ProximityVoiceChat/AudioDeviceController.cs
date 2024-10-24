﻿using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ProximityVoiceChat.Log;

namespace ProximityVoiceChat;

public class AudioDeviceController : IDisposable
{
    public bool IsAudioRecordingSourceActive => PlayingBackMicAudio || (!MuteMic && !Deafen && AudioRecordingIsRequested);
    public bool IsAudioPlaybackSourceActive => PlayingBackMicAudio || (!Deafen && AudioPlaybackIsRequested);

    public bool MuteMic
    {
        get => this.muteMic || this.Deafen;
        set
        {
            this.muteMic = this.configuration.MuteMic = value;
            if (!this.muteMic)
            {
                this.deafen = this.configuration.Deafen = false;
            }
            this.configuration.Save();
            UpdateSourceStates();
        }
    }
    private bool muteMic;

    public bool Deafen
    {
        get => this.deafen;
        set
        {
            this.deafen = this.configuration.Deafen = value;
            this.configuration.Save();
            UpdateSourceStates();
        }
    }
    private bool deafen;

    public bool PlayingBackMicAudio
    {
        get => this.playingBackMicAudio;
        set
        {
            this.playingBackMicAudio = value;
            ClearPlaybackBuffers(false);
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
                UpdateSourceStates();
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

                DisposeAudioPlaybackSource();
                UpdateSourceStates();
            }
        }
    }
    private int audioPlaybackDeviceIndex;

    public event EventHandler<WaveInEventArgs>? OnAudioRecordingSourceDataAvailable;
    public WaveInEventArgs? LastAudioRecordingSourceData;

    private class PlaybackChannel
    {
        public required VolumeSampleProvider VolumeSampleProvider { get; set; }
        public required BufferedWaveProvider BufferedWaveProvider { get; set; }
        public WaveInEventArgs? LastSampleAdded { get; set; }
    }

    private WaveInEvent? audioRecordingSource;
    private WaveOutEvent? audioPlaybackSource;
    private bool recording;
    private bool playingBack;

    private readonly WaveFormat waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
    private readonly Dictionary<string, PlaybackChannel> playbackChannels = [];
    private readonly BufferedWaveProvider micPlaybackWaveProvider;
    private readonly VolumeSampleProvider micPlaybackVolumeProvider;
    private readonly MixingSampleProvider outputSampleProvider;
    private readonly Configuration configuration;
    private readonly ILogger logger;

    public static byte[] ConvertAudioSampleToByteArray(WaveInEventArgs args)
    {
        var newArray = new byte[args.Buffer.Length + sizeof(int)];
        args.Buffer.CopyTo(newArray, sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(newArray, args.BytesRecorded);
        return newArray;
    }

    public static bool TryParseAudioSampleBytes(byte[] bytes, out WaveInEventArgs? args)
    {
        args = null;
        if (bytes.Length < sizeof(int))
        {
            return false;
        }
        Span<byte> bytesSpan = bytes;
        if (!BinaryPrimitives.TryReadInt32BigEndian(bytesSpan[..sizeof(int)], out var bytesRecorded))
        {
            return false;
        }
        args = new WaveInEventArgs(bytesSpan[sizeof(int)..].ToArray(), bytesRecorded);
        return true;
    }

    public AudioDeviceController(Configuration configuration, ILogger logger)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        this.muteMic = configuration.MuteMic;
        this.deafen = configuration.Deafen;
        this.audioRecordingDeviceIndex = configuration.SelectedAudioInputDeviceIndex;
        this.audioPlaybackDeviceIndex = configuration.SelectedAudioOutputDeviceIndex;

        this.micPlaybackWaveProvider = new BufferedWaveProvider(this.waveFormat);
        this.micPlaybackVolumeProvider = new VolumeSampleProvider(this.micPlaybackWaveProvider.ToSampleProvider());
        this.outputSampleProvider = new MixingSampleProvider(this.waveFormat)
        {
            ReadFully = true,
        };
        this.outputSampleProvider.AddMixerInput(this.micPlaybackVolumeProvider);
    }

    public void Dispose()
    {
        DisposeAudioRecordingSource();
        DisposeAudioPlaybackSource();
        GC.SuppressFinalize(this);
    }

    public IEnumerable<string> GetAudioRecordingDevices()
    {
        for (int n = -1; n < WaveIn.DeviceCount; n++)
        {
            var caps = WaveIn.GetCapabilities(n);
            if (n == -1)
            {
                yield return "Default";
            }
            else
            {
                yield return caps.ProductName;
            }
        }
    }

    public IEnumerable<string> GetAudioPlaybackDevices()
    {
        for (int n = -1; n < WaveOut.DeviceCount; n++)
        {
            var caps = WaveOut.GetCapabilities(n);
            if (n == -1)
            {
                yield return "Default";
            }
            else
            {
                yield return caps.ProductName;
            }
        }
    }

    public void CreateAudioPlaybackChannel(string channelName)
    {
        if (this.playbackChannels.ContainsKey(channelName))
        {
            this.logger.Error("An audio playback channel already exists for channel name {0}", channelName);
            return;
        }
        var bfp = new BufferedWaveProvider(this.waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(0.5),
            DiscardOnBufferOverflow = true,
        };
        var vsp = new VolumeSampleProvider(bfp.ToSampleProvider());
        this.outputSampleProvider.AddMixerInput(vsp);
        this.playbackChannels.Add(channelName, new()
        {
            VolumeSampleProvider = vsp,
            BufferedWaveProvider = bfp,
        });
    }

    public void RemoveAudioPlaybackChannel(string channelName)
    {
        if (this.playbackChannels.TryGetValue(channelName, out var channel))
        {
            channel.BufferedWaveProvider.ClearBuffer();
            this.outputSampleProvider.RemoveMixerInput(channel.VolumeSampleProvider);
            this.playbackChannels.Remove(channelName);
        }
    }

    public void AddPlaybackSample(string channelName, WaveInEventArgs sample)
    {
        if (this.playbackChannels.TryGetValue(channelName, out var channel))
        {
            channel.BufferedWaveProvider.AddSamples(sample.Buffer, 0, sample.BytesRecorded);
            channel.LastSampleAdded = sample;
        }
    }

    public void ResetAllChannelsVolume(float volume)
    {
        if (this.Deafen || this.PlayingBackMicAudio)
        {
            volume = 0.0f;
        }
        foreach(var channel in this.playbackChannels)
        {
            channel.Value.VolumeSampleProvider.Volume = volume;
        }
    }

    public void SetChannelVolume(string channelName, float volume)
    {
        if (this.Deafen || this.PlayingBackMicAudio)
        {
            volume = 0.0f;
        }
        if (this.playbackChannels.TryGetValue(channelName, out var channel))
        {
            channel.VolumeSampleProvider.Volume = volume;
        }
    }

    public bool ChannelHasActivity(string channelName)
    {
        if (this.playbackChannels.TryGetValue(channelName, out var channel))
        {
            if (channel.VolumeSampleProvider.Volume == 0.0f ||
                channel.BufferedWaveProvider.BufferedBytes == 0 ||
                channel.LastSampleAdded == null)
            {
                return false;
            }
            return channel.LastSampleAdded.Buffer.Any(b => b != default);
        }
        return false;
    }

    private WaveInEvent GetOrCreateAudioRecordingSource()
    {
        if (this.audioRecordingSource == null)
        {
            this.audioRecordingSource = new WaveInEvent
            {
                DeviceNumber = this.AudioRecordingDeviceIndex,
                WaveFormat = this.waveFormat,
            };

            this.audioRecordingSource.RecordingStopped += (object? sender, StoppedEventArgs e) =>
            {
                this.recording = false;
            };
            this.audioRecordingSource.DataAvailable += this.OnAudioSourceDataAvailable;

            this.recording = false;
        }
        return this.audioRecordingSource;
    }

    private void DisposeAudioRecordingSource()
    {
        if (this.audioRecordingSource != null)
        {
            this.audioRecordingSource.Dispose();
            this.audioRecordingSource = null;
        }
    }

    private WaveOutEvent GetOrCreateAudioPlaybackSource()
    {
        if (this.audioPlaybackSource == null)
        {
            this.audioPlaybackSource = new WaveOutEvent
            {
                DeviceNumber = this.AudioPlaybackDeviceIndex,
                DesiredLatency = 150,
            };
            this.audioPlaybackSource.PlaybackStopped += (object? sender, StoppedEventArgs e) =>
            {
                this.playingBack = false;
            };
            this.audioPlaybackSource.Init(this.outputSampleProvider);

            this.playingBack = false;
        }
        return this.audioPlaybackSource;
    }

    private void DisposeAudioPlaybackSource()
    {
        if (this.audioPlaybackSource != null)
        {
            this.audioPlaybackSource.Dispose();
            this.audioPlaybackSource = null;
        }
    }

    private void OnAudioSourceDataAvailable(object? sender, WaveInEventArgs e)
    {
        //this.logger.Trace("Audio data received from recording device: {0} bytes recorded, {1}", e.BytesRecorded, e.Buffer);
        if (this.audioPlaybackSource != null && this.PlayingBackMicAudio)
        {
            this.micPlaybackWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            this.micPlaybackVolumeProvider.Volume = this.configuration.MasterVolume;
        }
        this.LastAudioRecordingSourceData = e;
        this.OnAudioRecordingSourceDataAvailable?.Invoke(this, e);
    }

    private void ClearPlaybackBuffers(bool clearAll)
    {
        if (clearAll || this.PlayingBackMicAudio)
        {
            foreach(var channel in this.playbackChannels.Values)
            {
                channel.BufferedWaveProvider.ClearBuffer();
            }
        }
        if (clearAll || !this.PlayingBackMicAudio)
        {
            this.micPlaybackWaveProvider.ClearBuffer();
        }
    }

    private void UpdateSourceStates()
    {
        if (this.IsAudioRecordingSourceActive)
        {
            if (!this.recording)
            {
                this.logger.Debug("Starting audio recording source from device #{0}", GetOrCreateAudioRecordingSource().DeviceNumber);
                GetOrCreateAudioRecordingSource().StartRecording();
                this.recording = true;
            }
        }
        else
        {
            this.logger.Debug("Stopping audio recording source from device #{0}", GetOrCreateAudioRecordingSource().DeviceNumber);
            GetOrCreateAudioRecordingSource().StopRecording();
            this.LastAudioRecordingSourceData = null;
            this.recording = false;
        }

        if (this.IsAudioPlaybackSourceActive)
        {
            if (!this.playingBack)
            {
                this.logger.Debug("Starting audio playback source from device #{0}", GetOrCreateAudioPlaybackSource().DeviceNumber);
                ClearPlaybackBuffers(true);
                GetOrCreateAudioPlaybackSource().Play();
                this.playingBack = true;
            }
        }
        else
        {
            this.logger.Debug("Stopping audio playback source from device #{0}", GetOrCreateAudioPlaybackSource().DeviceNumber);
            GetOrCreateAudioPlaybackSource().Stop();
            this.playingBack = false;
        }
    }
}
