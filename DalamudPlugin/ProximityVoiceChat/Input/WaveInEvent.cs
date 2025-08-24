using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Mixer;
using NAudio.Wave;

namespace ProximityVoiceChat.Input;

public class WaveInEvent : IWaveIn, IDisposable
{
    private readonly AutoResetEvent callbackEvent;

    private readonly SynchronizationContext? syncContext;

    private IntPtr waveInHandle;

    private volatile CaptureState captureState;

    private WaveInBuffer[]? buffers;

    //
    // Summary:
    //     Returns the number of Wave In devices available in the system
    public static int DeviceCount => WaveInterop.waveInGetNumDevs();

    //
    // Summary:
    //     Milliseconds for the buffer. Recommended value is 100ms
    public int BufferMilliseconds { get; set; }

    //
    // Summary:
    //     Number of Buffers to use (usually 2 or 3)
    public int NumberOfBuffers { get; set; }

    //
    // Summary:
    //     The device number to use
    public int DeviceNumber { get; set; }

    //
    // Summary:
    //     WaveFormat we are recording in
    public WaveFormat WaveFormat { get; set; }

    //
    // Summary:
    //     Indicates recorded data is available
    public event EventHandler<WaveInEventArgs>? DataAvailable;

    //
    // Summary:
    //     Indicates that all recorded data has now been received.
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    //
    // Summary:
    //     Prepares a Wave input device for recording
    public WaveInEvent()
    {
        callbackEvent = new AutoResetEvent(initialState: false);
        syncContext = SynchronizationContext.Current;
        DeviceNumber = 0;
        WaveFormat = new WaveFormat(8000, 16, 1);
        BufferMilliseconds = 100;
        NumberOfBuffers = 3;
        captureState = CaptureState.Stopped;
    }

    //
    // Summary:
    //     Retrieves the capabilities of a waveIn device
    //
    // Parameters:
    //   devNumber:
    //     Device to test
    //
    // Returns:
    //     The WaveIn device capabilities
    public static WaveInCapabilities GetCapabilities(int devNumber)
    {
        WaveInCapabilities waveInCaps = default(WaveInCapabilities);
        int waveInCapsSize = Marshal.SizeOf(waveInCaps);
        MmException.Try(WaveInterop.waveInGetDevCaps((IntPtr)devNumber, out waveInCaps, waveInCapsSize), "waveInGetDevCaps");
        return waveInCaps;
    }

    private void CreateBuffers()
    {
        int num = BufferMilliseconds * WaveFormat.AverageBytesPerSecond / 1000;
        if (num % WaveFormat.BlockAlign != 0)
        {
            num -= num % WaveFormat.BlockAlign;
        }

        buffers = new WaveInBuffer[NumberOfBuffers];
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = new WaveInBuffer(waveInHandle, num);
        }
    }

    private void OpenWaveInDevice()
    {
        CloseWaveInDevice();
        MmException.Try(WaveInterop.waveInOpenWindow(out waveInHandle, (IntPtr)DeviceNumber, WaveFormat, callbackEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, WaveInterop.WaveInOutOpenFlags.CallbackEvent), "waveInOpen");
        CreateBuffers();
    }

    //
    // Summary:
    //     Start recording
    public void StartRecording()
    {
        if (captureState != 0)
        {
            throw new InvalidOperationException("Already recording");
        }

        OpenWaveInDevice();
        MmException.Try(WaveInterop.waveInStart(waveInHandle), "waveInStart");
        captureState = CaptureState.Starting;
        ThreadPool.QueueUserWorkItem(delegate
        {
            RecordThread();
        }, null);
    }

    private void RecordThread()
    {
        Exception? e = null;
        try
        {
            DoRecording();
        }
        catch (Exception ex)
        {
            e = ex;
        }
        finally
        {
            captureState = CaptureState.Stopped;
            RaiseRecordingStoppedEvent(e);
        }
    }

    private void DoRecording()
    {
        if (buffers == null) { return; }

        captureState = CaptureState.Capturing;
        WaveInBuffer[] array = buffers;
        foreach (WaveInBuffer waveInBuffer in array)
        {
            if (!waveInBuffer.InQueue)
            {
                waveInBuffer.Reuse();
            }
        }

        while (captureState == CaptureState.Capturing)
        {
            if (!callbackEvent.WaitOne())
            {
                continue;
            }

            if (captureState != CaptureState.Capturing)
            {
                return;
            }

            array = buffers;
            foreach (WaveInBuffer waveInBuffer2 in array)
            {
                if (waveInBuffer2.Done)
                {
                    if (waveInBuffer2.BytesRecorded > 0)
                    {
                        this.DataAvailable?.Invoke(this, new WaveInEventArgs(waveInBuffer2.Data, waveInBuffer2.BytesRecorded));
                    }

                    if (captureState == CaptureState.Capturing)
                    {
                        waveInBuffer2.Reuse();
                    }
                }
            }
        }
    }

    private void RaiseRecordingStoppedEvent(Exception e)
    {
        EventHandler<StoppedEventArgs>? handler = this.RecordingStopped;
        if (handler == null)
        {
            return;
        }

        if (syncContext == null)
        {
            handler(this, new StoppedEventArgs(e));
            return;
        }

        syncContext.Post(delegate
        {
            handler(this, new StoppedEventArgs(e));
        }, null);
    }

    //
    // Summary:
    //     Stop recording
    public void StopRecording()
    {
        if (captureState != CaptureState.Stopped && captureState != CaptureState.Stopping)
        {
            captureState = CaptureState.Stopping;
            MmException.Try(WaveInterop.waveInStop(waveInHandle), "waveInStop");
            MmException.Try(WaveInterop.waveInReset(waveInHandle), "waveInReset");
            callbackEvent.Set();
        }
    }

    //
    // Summary:
    //     Gets the current position in bytes from the wave input device. it calls directly
    //     into waveInGetPosition)
    //
    // Returns:
    //     Position in bytes
    public long GetPosition()
    {
        MmTime mmTime = default(MmTime);
        mmTime.wType = 4u;
        MmException.Try(WaveInterop.waveInGetPosition(waveInHandle, out mmTime, Marshal.SizeOf(mmTime)), "waveInGetPosition");
        if (mmTime.wType != 4)
        {
            throw new Exception($"waveInGetPosition: wType -> Expected {4}, Received {mmTime.wType}");
        }

        return mmTime.cb;
    }

    //
    // Summary:
    //     Dispose pattern
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (captureState != 0)
            {
                StopRecording();
            }

            CloseWaveInDevice();
        }
    }

    private void CloseWaveInDevice()
    {
        WaveInterop.waveInReset(waveInHandle);
        if (buffers != null)
        {
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i].Dispose();
            }

            buffers = null;
        }

        WaveInterop.waveInClose(waveInHandle);
        waveInHandle = IntPtr.Zero;
    }

    //
    // Summary:
    //     Microphone Level
    public MixerLine GetMixerLine()
    {
        if (waveInHandle != IntPtr.Zero)
        {
            return new MixerLine(waveInHandle, 0, MixerFlags.WaveInHandle);
        }

        return new MixerLine((IntPtr)DeviceNumber, 0, MixerFlags.WaveIn);
    }

    //
    // Summary:
    //     Dispose method
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
