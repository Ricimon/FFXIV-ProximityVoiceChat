using System;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.Wave;

namespace ProximityVoiceChat.Input;

//
// Summary:
//     A buffer of Wave samples
public class WaveInBuffer : IDisposable
{
    private readonly WaveHeader header;

    private readonly int bufferSize;

    private readonly byte[] buffer;

    private GCHandle hBuffer;

    private nint waveInHandle;

    private GCHandle hHeader;

    private GCHandle hThis;

    //
    // Summary:
    //     Provides access to the actual record buffer (for reading only)
    public byte[] Data => buffer;

    //
    // Summary:
    //     Indicates whether the Done flag is set on this buffer
    public bool Done => (header.flags & WaveHeaderFlags.Done) == WaveHeaderFlags.Done;

    //
    // Summary:
    //     Indicates whether the InQueue flag is set on this buffer
    public bool InQueue => (header.flags & WaveHeaderFlags.InQueue) == WaveHeaderFlags.InQueue;

    //
    // Summary:
    //     Number of bytes recorded
    public int BytesRecorded => header.bytesRecorded;

    //
    // Summary:
    //     The buffer size in bytes
    public int BufferSize => bufferSize;

    //
    // Summary:
    //     creates a new wavebuffer
    //
    // Parameters:
    //   waveInHandle:
    //     WaveIn device to write to
    //
    //   bufferSize:
    //     Buffer size in bytes
    public WaveInBuffer(nint waveInHandle, int bufferSize)
    {
        this.bufferSize = bufferSize;
        buffer = new byte[bufferSize];
        hBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        this.waveInHandle = waveInHandle;
        header = new WaveHeader();
        hHeader = GCHandle.Alloc(header, GCHandleType.Pinned);
        header.dataBuffer = hBuffer.AddrOfPinnedObject();
        header.bufferLength = bufferSize;
        header.loops = 1;
        hThis = GCHandle.Alloc(this);
        header.userData = (nint)hThis;
        MmException.Try(WaveInterop.waveInPrepareHeader(waveInHandle, header, Marshal.SizeOf(header)), "waveInPrepareHeader");
    }

    //
    // Summary:
    //     Place this buffer back to record more audio
    public void Reuse()
    {
        MmException.Try(WaveInterop.waveInUnprepareHeader(waveInHandle, header, Marshal.SizeOf(header)), "waveUnprepareHeader");
        MmException.Try(WaveInterop.waveInPrepareHeader(waveInHandle, header, Marshal.SizeOf(header)), "waveInPrepareHeader");
        MmException.Try(WaveInterop.waveInAddBuffer(waveInHandle, header, Marshal.SizeOf(header)), "waveInAddBuffer");
    }

    //
    // Summary:
    //     Finalizer for this wave buffer
    ~WaveInBuffer()
    {
        Dispose(disposing: false);
    }

    //
    // Summary:
    //     Releases resources held by this WaveBuffer
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(disposing: true);
    }

    //
    // Summary:
    //     Releases resources held by this WaveBuffer
    protected void Dispose(bool disposing)
    {
        if (waveInHandle != nint.Zero)
        {
            MmException.Try(WaveInterop.waveInUnprepareHeader(waveInHandle, header, Marshal.SizeOf(header)), "waveUnprepareHeader");
            waveInHandle = nint.Zero;
        }

        if (hHeader.IsAllocated)
        {
            hHeader.Free();
        }

        if (hBuffer.IsAllocated)
        {
            hBuffer.Free();
        }

        if (hThis.IsAllocated)
        {
            hThis.Free();
        }
    }
}
