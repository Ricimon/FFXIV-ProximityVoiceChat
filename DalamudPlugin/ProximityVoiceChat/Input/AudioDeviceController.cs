using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ProximityVoiceChat.Log;
using RNNoise.NET;
using WebRtcVadSharp;

namespace ProximityVoiceChat.Input;

public class AudioDeviceController : IAudioDeviceController, IDisposable
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
    public bool RecordingDataHasActivity => this.lastAudioRecordingSourceData != null &&
        (this.configuration.SuppressNoise ?
            this.selfVoiceActivityDetector.HasSpeech(this.lastAudioRecordingSourceData.Buffer) :
            this.lastAudioRecordingSourceData.Buffer.Any(b => b != default));

    private WaveInEvent? audioRecordingSource;
    private WaveOutEvent? audioPlaybackSource;
    private Denoiser? denoiser;
    private bool recording;
    private bool playingBack;
    private WaveInEventArgs? lastAudioRecordingSourceData;
    private CachedSoundSampleProvider? currentSfx;
    private TaskCompletionSource? currentSfxTcs;

    private const int SampleRate = 48000; // RNNoise frequency
    private const int FrameLength = 20; // 20 ms, for max compatibility
    private const int WaveOutDesiredLatency = 200;
    private const int WaveOutNumberOfBuffers = 5;
    private const int MinimumBufferClearIntervalMs = 5000;

    private readonly WaveFormat waveFormat = new(rate: 48000, bits: 16, channels: 1);
    private readonly Dictionary<string, PlaybackChannel> playbackChannels = [];
    private readonly int maxPlaybackChannelBufferSize;
    private readonly BufferedWaveProvider micPlaybackWaveProvider;
    private readonly VolumeSampleProvider micPlaybackVolumeProvider;
    private readonly MixingSampleProvider outputSampleProvider;
    private readonly float[] denoiserFloatSamples = new float[GetSampleSize(SampleRate, FrameLength, 1) / 2];
    private readonly WebRtcVad selfVoiceActivityDetector = new()
    {
        FrameLength = WebRtcVadSharp.FrameLength.Is20ms,
        SampleRate = WebRtcVadSharp.SampleRate.Is48kHz,
    };
    private readonly Configuration configuration;
    private readonly ILogger logger;

#if DEBUG
    // Use this to simulate lag (can only use when there's 1 other user in the voice room)
    private readonly List<WaveInEventArgs> inputQueue = new();
#endif

    public static byte[] ConvertAudioSampleToByteArray(WaveInEventArgs args)
    {
        var newArray = new byte[args.Buffer.Length + sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(newArray, (ushort)args.BytesRecorded);
        args.Buffer.CopyTo(newArray, sizeof(ushort));
        return newArray;
    }

    public static bool TryParseAudioSampleBytes(byte[] bytes, out WaveInEventArgs? args)
    {
        args = null;
        if (bytes.Length < sizeof(ushort))
        {
            return false;
        }
        Span<byte> bytesSpan = bytes;
        if (!BinaryPrimitives.TryReadUInt16BigEndian(bytesSpan[..sizeof(ushort)], out var bytesRecorded))
        {
            return false;
        }
        args = new WaveInEventArgs(bytesSpan[sizeof(ushort)..].ToArray(), bytesRecorded);
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

        // This is how buffer size is calculated in WaveOutEvent
        this.maxPlaybackChannelBufferSize = this.waveFormat.ConvertLatencyToByteSize((WaveOutDesiredLatency + WaveOutNumberOfBuffers - 1) / WaveOutNumberOfBuffers) * WaveOutNumberOfBuffers;

        this.micPlaybackWaveProvider = new BufferedWaveProvider(this.waveFormat);
        this.micPlaybackVolumeProvider = new VolumeSampleProvider(this.micPlaybackWaveProvider.ToSampleProvider());
        this.outputSampleProvider = new MixingSampleProvider([this.micPlaybackVolumeProvider])
        {
            ReadFully = true,
        };
        this.outputSampleProvider.MixerInputEnded += OnMixerInputEnded;
    }

    public void Dispose()
    {
        DisposeAudioRecordingSource();
        DisposeAudioPlaybackSource();
        this.outputSampleProvider.MixerInputEnded -= OnMixerInputEnded;
        this.currentSfxTcs?.TrySetCanceled();
        this.denoiser?.Dispose();
        this.selfVoiceActivityDetector.Dispose();
        foreach(var channel in this.playbackChannels.Values)
        {
            channel.Dispose();
        }
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
            DiscardOnBufferOverflow = true,
            BufferLength = this.waveFormat.AverageBytesPerSecond * 1,
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
            channel.Dispose();
        }
    }

    public void AddPlaybackSample(string channelName, WaveInEventArgs sample)
    {
//#if DEBUG
//        AddPlaybackSampleDebug(channelName, sample, true);
//        return;
//#endif
        if (this.playbackChannels.TryGetValue(channelName, out var channel))
        {
            var now = Environment.TickCount;
            // If the output device cannot read from the playback buffer as fast as it is filled,
            // then the playback buffer can get filled and introduce audio latency.
            // This can occur during high system load.
            // To remove this latency, we ensure the playback buffer never goes above the expected buffer size,
            // calculated from the intended output device latency and buffer count.
            if (channel.BufferedWaveProvider.BufferedBytes + sample.BytesRecorded > this.maxPlaybackChannelBufferSize)
            {
                // However, don't clear too often as this can cause audio "roboting"
                var timeSinceLastBufferClear = now - channel.BufferClearedEventTimestampMs;
                if (timeSinceLastBufferClear > MinimumBufferClearIntervalMs)
                {
                    channel.BufferedWaveProvider.ClearBuffer();
                    channel.BufferClearedEventTimestampMs = now;
                }
            }
            channel.BufferedWaveProvider.AddSamples(sample.Buffer, 0, sample.BytesRecorded);
            channel.LastSampleAdded = sample;
            channel.LastSampleAddedTimestampMs = now;
        }
    }

//#if DEBUG
//    public void AddPlaybackSampleDebug(string channelName, WaveInEventArgs sample, bool queue)
//    {
//        if (queue)
//        {
//            // Simulated latency
//            inputQueue.Add(sample);
//            if (inputQueue.Count > 50)
//            {
//                foreach (var s in inputQueue)
//                {
//                    AddPlaybackSampleDebug(channelName, s, false);
//                }
//                inputQueue.Clear();
//            }
//            return;
//        }

//        if (this.playbackChannels.TryGetValue(channelName, out var channel))
//        {
//            var now = Environment.TickCount;
//            // If the output device cannot read from the playback buffer as fast as it is filled,
//            // then the playback buffer can get filled and introduce audio latency.
//            // This can occur during high system load.
//            // To remove this latency, we ensure the playback buffer never goes above the expected buffer size,
//            // calculated from the intended output device latency and buffer count.
//            if (channel.BufferedWaveProvider.BufferedBytes + sample.BytesRecorded > this.maxPlaybackChannelBufferSize)
//            {
//                // However, don't clear too often as this can cause audio "roboting"
//                var timeSinceLastBufferClear = now - channel.BufferClearedEventTimestampMs;
//                if (timeSinceLastBufferClear > MinimumBufferClearIntervalMs)
//                {
//                    channel.BufferedWaveProvider.ClearBuffer();
//                    channel.BufferClearedEventTimestampMs = now;
//                }
//            }
//            channel.BufferedWaveProvider.AddSamples(sample.Buffer, 0, sample.BytesRecorded);
//            this.logger.Debug("Buffer state: buffered bytes: {0}, buffer length: {1}", channel.BufferedWaveProvider.BufferedBytes, channel.BufferedWaveProvider.BufferLength);
//            channel.LastSampleAdded = sample;
//            channel.LastSampleAddedTimestampMs = now;
//        }
//    }
//#endif

    public void ResetAllChannelsVolume(float volume)
    {
        if (this.Deafen || this.PlayingBackMicAudio)
        {
            volume = 0.0f;
        }
        foreach (var channel in this.playbackChannels)
        {
            if (volume != 0.0f && this.configuration.PeerVolumes.TryGetValue(channel.Key, out var peerVolume))
            {
                volume *= peerVolume;
            }
            channel.Value.VolumeSampleProvider.Volume = volume;
        }
    }

    public void SetChannelVolume(string channelName, float volume)
    {
        if (this.Deafen || this.PlayingBackMicAudio)
        {
            volume = 0.0f;
        }
        if (volume != 0.0f && this.configuration.PeerVolumes.TryGetValue(channelName, out var peerVolume))
        {
            volume *= peerVolume;
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
            if (channel.VolumeSampleProvider.Volume == 0.0f)
            {
                return false;
            }
            if (channel.LastSampleAdded == null)
            {
                return false;
            }
            // Recording the timestamp of the last added sample allows us to keep the sample valid
            // for activity purposes for longer.
            // This patches an issue where a buffer read would clear the buffer and indicate no
            // channel activity until the next sample was added (this would manifest as a rapidly
            // blinking activity indicator if the read/write buffers were small enough)
            if (channel.LastSampleAddedTimestampMs + 100 < Environment.TickCount &&
                channel.BufferedWaveProvider.BufferedBytes == 0)
            {
                return false;
            }
            return channel.VoiceActivityDetector.HasSpeech(channel.LastSampleAdded.Buffer);
        }
        return false;
    }

    /// <returns>A Task that completes when the sound fully finished playing, and cancels if the sound is interrupted.</returns>
    public Task PlaySfx(CachedSound sound)
    {
        if (this.currentSfx != null)
        {
            this.outputSampleProvider.RemoveMixerInput(this.currentSfx);
            this.currentSfxTcs?.TrySetCanceled();
        }
        this.currentSfx = new CachedSoundSampleProvider(sound);
        this.outputSampleProvider.AddMixerInput(this.currentSfx);
        this.currentSfxTcs = new TaskCompletionSource();
        return this.currentSfxTcs.Task;
    }

    private WaveInEvent? GetAudioRecordingSource(bool createIfNull)
    {
        if (this.audioRecordingSource == null && createIfNull)
        {
            this.audioRecordingSource = new WaveInEvent
            {
                DeviceNumber = this.AudioRecordingDeviceIndex,
                WaveFormat = this.waveFormat,
                BufferMilliseconds = 20, // 20 ms for max compatibility
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

    private WaveOutEvent? GetAudioPlaybackSource(bool createIfNull)
    {
        if (this.audioPlaybackSource == null && createIfNull)
        {
            this.audioPlaybackSource = new WaveOutEvent
            {
                DeviceNumber = this.AudioPlaybackDeviceIndex,
                DesiredLatency = WaveOutDesiredLatency,
                NumberOfBuffers = WaveOutNumberOfBuffers,
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

    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        if (e.SampleProvider == this.currentSfx)
        {
            this.currentSfxTcs?.TrySetResult();
            this.currentSfx = null;
            this.currentSfxTcs = null;
        }
    }

    private void OnAudioSourceDataAvailable(object? sender, WaveInEventArgs e)
    {
        //this.logger.Trace("Audio data received from recording device: {0} bytes recorded, {1}", e.BytesRecorded, e.Buffer);
        if (this.configuration.SuppressNoise && this.denoiser != null)
        {
            Convert16BitToFloat(e.Buffer, this.denoiserFloatSamples);
            this.denoiser.Denoise(this.denoiserFloatSamples);
            ConvertFloatTo16Bit(this.denoiserFloatSamples, e.Buffer);
        }
        if (this.audioPlaybackSource != null && this.PlayingBackMicAudio)
        {
            this.micPlaybackWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            this.micPlaybackVolumeProvider.Volume = this.configuration.MasterVolume;
        }
        this.lastAudioRecordingSourceData = e;
        this.OnAudioRecordingSourceDataAvailable?.Invoke(this, e);
    }

    private void ClearPlaybackBuffers(bool clearAll)
    {
        if (clearAll || this.PlayingBackMicAudio)
        {
            foreach (var channel in this.playbackChannels.Values)
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
                this.logger.Debug("Starting audio recording source from device {0}", GetAudioRecordingSource(true)!.DeviceNumber);
                GetAudioRecordingSource(true)!.StartRecording();
                // We need a new denoiser here as the previous denoiser may have remaining incomplete audio data
                // that can cause audio popping the next time it is used.
                this.denoiser?.Dispose(); this.denoiser = null;
                this.denoiser = new();
                this.recording = true;
            }
        }
        else
        {
            var recordingSource = GetAudioRecordingSource(false);
            if (recordingSource != null)
            {
                this.logger.Debug("Stopping audio recording source from device {0}", recordingSource.DeviceNumber);
                recordingSource.StopRecording();
            }
            this.lastAudioRecordingSourceData = null;
            this.denoiser?.Dispose(); this.denoiser = null;
            this.recording = false;
        }

        if (this.IsAudioPlaybackSourceActive)
        {
            if (!this.playingBack)
            {
                this.logger.Debug("Starting audio playback source from device {0}", GetAudioPlaybackSource(true)!.DeviceNumber);
                ClearPlaybackBuffers(true);
                GetAudioPlaybackSource(true)!.Play();
                this.playingBack = true;
            }
        }
        else
        {
            var playbackSource = GetAudioPlaybackSource(false);
            if (playbackSource != null)
            {
                this.logger.Debug("Stopping audio playback source from device {0}", playbackSource.DeviceNumber);
                playbackSource.Stop();
            }
            if (this.currentSfx != null)
            {
                this.outputSampleProvider.RemoveMixerInput(this.currentSfx);
                this.currentSfxTcs?.TrySetCanceled();
                this.currentSfx = null;
                this.currentSfxTcs = null;
            }
            this.playingBack = false;
        }
    }

    // Utility methods taken from https://github.com/realcoloride/OpenVoiceSharp/blob/master/VoiceUtilities.cs

    /// <summary>
    /// Gets the sample size for a frame.
    /// </summary>
    /// <param name="channels">Set 1 for mono and 2 for stereo</param>
    /// <param name="float32">Float32 size is half</param>
    /// <returns></returns>
    private static int GetSampleSize(int sampleRate, int timeLengthMs, int channels)
        => ((int)(sampleRate * 16f / 8f * (timeLengthMs / 1000f) * channels));

    /// <summary>
    /// Converts 16 bit PCM data into float 32.
    /// Note that the float array must be half the size of the byte array.
    /// </summary>
    /// <param name="input">The 16 bit PCM data according to your needs.</param>
    /// <param name="output">The output data in which the result will be returned.</param>
    /// <returns>The 16 bit byte array.</returns>
    private static void Convert16BitToFloat(byte[] input, float[] output)
    {
        int outputIndex = 0;
        short sample;

        for (int n = 0; n < output.Length; n++)
        {
            sample = BitConverter.ToInt16(input, n * 2);
            output[outputIndex++] = sample / 32768f;
        }
    }

    /// <summary>
    /// Converts float 32 PCM data into 16 bit.
    /// Note that the byte array must be double the size of the float array.
    /// </summary>
    /// <param name="input">The float 32 PCM data according to your needs.</param>
    /// <param name="output">The output data in which the result will be returned.</param>
    /// <returns>The float32 PCM array.</returns>
    private static void ConvertFloatTo16Bit(float[] input, byte[] output)
    {
        int sampleIndex = 0, pcmIndex = 0;

        while (sampleIndex < input.Length)
        {
            // Math.Clamp solution found from https://github.com/mumble-voip/mumble/pull/5363
            short outsample = (short)Math.Clamp(input[sampleIndex] * short.MaxValue, short.MinValue, short.MaxValue);
            output[pcmIndex] = (byte)(outsample & 0xff);
            output[pcmIndex + 1] = (byte)((outsample >> 8) & 0xff);

            sampleIndex++;
            pcmIndex += 2;
        }
    }
}
