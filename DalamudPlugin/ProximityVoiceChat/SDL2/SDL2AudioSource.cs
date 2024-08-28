using ProximityVoiceChat.Log;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.SDL2;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace ProximityVoiceChat.SDL2;

public class SDL2AudioSource : IAudioSource
{
    private ILogger log;

    [AllowNull]
    private string _audioInDeviceName;
    private uint _audioInDeviceId = 0;

    private IAudioEncoder _audioEncoder;
    private MediaFormatManager<AudioFormat> _audioFormatManager;

    private bool _isStarted = false;
    private bool _isPaused = true;
    private bool _isClosed = true;

    private uint frameSize = 0;

    private BackgroundWorker backgroundWorker;

    private AudioSamplingRatesEnum audioSamplingRates;

    #region EVENT

    public event EncodedSampleDelegate? OnAudioSourceEncodedSample = null;
    public event RawAudioSampleDelegate? OnAudioSourceRawSample = null;
    public event SourceErrorDelegate? OnAudioSourceError = null;

    #endregion EVENT

    public SDL2AudioSource([AllowNull] string audioInDeviceName, IAudioEncoder audioEncoder, ILogger log, uint frameSize = 1920)
    {
        if (audioEncoder == null)
            throw new ApplicationException("Audio encoder provided is null");

        this.log = log;

        _audioInDeviceName = audioInDeviceName;

        _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
        _audioEncoder = audioEncoder;

        this.frameSize = frameSize;

        backgroundWorker = new BackgroundWorker();
        backgroundWorker.DoWork += BackgroundWorker_DoWork;
        backgroundWorker.WorkerSupportsCancellation = true;
    }

    private void RaiseAudioSourceError(string err)
    {
        CloseAudio();
        OnAudioSourceError?.Invoke(err);
    }

    private unsafe void BackgroundWorker_DoWork(object? sender, DoWorkEventArgs e)
    {
        while (!backgroundWorker.CancellationPending)
        {
            uint size = 0;
            uint bufferSize = 0;
            do
            {
                // Check if device is not stopped
                if (SDL2Helper.IsDeviceStopped(_audioInDeviceId))
                {
                    RaiseAudioSourceError($"SDLAudioSource [{_audioInDeviceName}] stoppped.");
                    return;
                }

                size = SDL2Helper.GetQueuedAudioSize(_audioInDeviceId);
                if (size >= frameSize * 2) // Need to use double size since we get byte[] and not short[] from SDL
                {
                    if (frameSize != 0)
                        bufferSize = frameSize * 2;
                    else
                        bufferSize = size;

                    byte[] buf = new byte[bufferSize];

                    fixed (byte* ptr = &buf[0])
                    {
                        SDL2Helper.DequeueAudio(_audioInDeviceId, (nint)ptr, bufferSize);

                        short[] pcm = buf.Take((int)bufferSize * 2).Where((x, i) => i % 2 == 0).Select((y, i) => BitConverter.ToInt16(buf, i * 2)).ToArray();
                        OnAudioSourceRawSample?.Invoke(audioSamplingRates, (uint)pcm.Length, pcm);

                        if (OnAudioSourceEncodedSample != null)
                        {
                            var encodedSample = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);
                            if (encodedSample.Length > 0)
                                OnAudioSourceEncodedSample?.Invoke((uint)(pcm.Length * _audioFormatManager.SelectedFormat.RtpClockRate / _audioFormatManager.SelectedFormat.ClockRate), encodedSample);
                        }
                    }

                    size -= bufferSize;
                }
            } while (size >= frameSize);

            SDL2Helper.Delay(16);
        }
        log.Trace("[BackgroundWorker_DoWork] Background worker cancellation.");
    }

    private void InitRecordingDevice()
    {
        try
        {
            // Stop previous recording device
            CloseAudio();

            // Init recording device.
            AudioFormat audioFormat = _audioFormatManager.SelectedFormat;
            if (audioFormat.ClockRate == AudioFormat.DEFAULT_CLOCK_RATE * 2)
                audioSamplingRates = AudioSamplingRatesEnum.Rate16KHz;
            else
                audioSamplingRates = AudioSamplingRatesEnum.Rate8KHz;

            var audioSpec = SDL2Helper.GetAudioSpec(audioFormat.ClockRate, 1, (ushort)frameSize);
            //int bytesPerSecond = SDL2Helper.GetBytesPerSecond(audioSpec);

            _audioInDeviceId = SDL2Helper.OpenAudioRecordingDevice(_audioInDeviceName, ref audioSpec);
            if (_audioInDeviceId > 0)
                log.Debug($"[InitRecordingDevice] Audio source - Id:[{_audioInDeviceId}] - DeviceName:[{_audioInDeviceName}]");
            else
            {
                log.Error($"[InitRecordingDevice] SDLAudioSource failed to initialise device. No audio device found with [{_audioInDeviceName}] and [{_audioInDeviceId}]");
                RaiseAudioSourceError($"SDLAudioSource failed to initialise device. No audio device found with [{_audioInDeviceName}] and [{_audioInDeviceId}]");
            }
        }
        catch (Exception excp)
        {
            log.Error($"[InitRecordingDevice] SDLAudioSource failed to initialise device [{_audioInDeviceName}] - [{_audioInDeviceId}] - Exception: [{excp.Message}]");
            RaiseAudioSourceError($"SDLAudioSource failed to initialise device [{_audioInDeviceName}] and [{_audioInDeviceId}] - Exception:[{excp.Message}]");
        }
    }

    public Task PauseAudio()
    {
        if (_isStarted && !_isPaused)
        {
            if (backgroundWorker.IsBusy)
                backgroundWorker.CancelAsync();

            if (_audioInDeviceId > 0)
                SDL2Helper.PauseAudioRecordingDevice(_audioInDeviceId, true);

            _isPaused = true;
            log.Debug($"[PauseAudio] Audio source - Id:[{_audioInDeviceId}]");
        }

        return Task.CompletedTask;
    }

    public Task ResumeAudio()
    {
        if (_isStarted && _isPaused)
        {
            if (!backgroundWorker.IsBusy)
                backgroundWorker.RunWorkerAsync();

            if (_audioInDeviceId > 0)
                SDL2Helper.PauseAudioRecordingDevice(_audioInDeviceId, false);

            _isPaused = false;
            log.Debug($"[ResumeAudio] Audio source - Id:[{_audioInDeviceId}]");
        }

        return Task.CompletedTask;
    }

    public bool IsAudioSourcePaused()
    {
        return _isPaused;
    }

    public Task StartAudio()
    {
        if (!_isStarted)
        {
            if (_audioInDeviceId > 0)
            {
                _isStarted = true;
                _isClosed = false;
                _isPaused = true;

                ResumeAudio().Wait();
            }
        }

        return Task.CompletedTask;
    }

    public Task CloseAudio()
    {
        if (_isStarted)
        {
            PauseAudio().Wait();
            if (_audioInDeviceId > 0)
            {
                SDL2Helper.CloseAudioRecordingDevice(_audioInDeviceId);
                log.Debug($"[CloseAudio] Audio source - Id:[{_audioInDeviceId}] - Namz:[{_audioInDeviceName}]");
            }
        }

        _isClosed = true;
        _isStarted = false;
        _audioInDeviceId = 0;

        return Task.CompletedTask;
    }

    public List<AudioFormat> GetAudioSourceFormats()
    {
        if (_audioFormatManager != null)
            return _audioFormatManager.GetSourceFormats();
        return new List<AudioFormat>();
    }

    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        if (_audioFormatManager != null)
        {
            log.Debug($"Setting audio source format to {audioFormat.FormatID}:{audioFormat.FormatName} {audioFormat.ClockRate}.");
            _audioFormatManager.SetSelectedFormat(audioFormat);

            InitRecordingDevice();
            StartAudio();
        }
    }

    public void RestrictFormats(Func<AudioFormat, bool> filter)
    {
        if (_audioFormatManager != null)
            _audioFormatManager.RestrictFormats(filter);
    }

    public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample) => throw new NotImplementedException();

    public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
}
