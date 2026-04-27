using System.Runtime.InteropServices;

namespace LocalLizard.Voice.Capture;

/// <summary>
/// P/Invoke wrapper around ALSA libasound for audio capture.
/// Captures 16KHz mono S16_LE from a specified device.
/// Uses snd_pcm_set_params for clean hardware parameter negotiation.
/// </summary>
public sealed class AlsaCapture : IDisposable
{
    // ── P/Invoke declarations ──

    private const string LibAsound = "libasound.so.2";

    // Stream type: SND_PCM_STREAM_CAPTURE = 1
    private const int StreamCapture = 1;
    // Access mode: SND_PCM_ACCESS_RW_INTERLEAVED = 3
    private const int AccessInterleaved = 3;
    // Format: SND_PCM_FORMAT_S16_LE = 2
    private const int FormatS16Le = 2;

    // Error codes
    private const int Epipe = -32; // Buffer overrun (xrun)

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_set_params(
        IntPtr pcm,
        int format,
        int access,
        int channels,
        int rate,
        int softResample,
        int latency
    );

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_readi(IntPtr pcm, IntPtr buffer, int frameCount);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(LibAsound, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_strerror(int errnum);

    // ── Instance state ──

    private IntPtr _pcm;
    private bool _disposed;

    /// <summary>Sample rate in Hz (fixed at 16KHz per design).</summary>
    public const int SampleRate = 16000;

    /// <summary>Number of audio channels (fixed at mono).</summary>
    public const int Channels = 1;

    /// <summary>Bytes per frame (S16_LE = 2 bytes × 1 channel).</summary>
    public const int FrameSize = 2;

    /// <summary>Bytes per second of audio.</summary>
    public const int BytesPerSecond = SampleRate * FrameSize;

    /// <summary>ALSA device name (e.g., "hw:1,0" or "plughw:CARD=RC08,DEV=0").</summary>
    public string DeviceName { get; }

    /// <summary>
    /// Open a capture stream on the specified ALSA device.
    /// </summary>
    /// <param name="deviceName">
    /// ALSA PCM device name. Default "hw:1,0" for RC08 webcam.
    /// Use "plughw:1,0" for automatic format conversion if needed.
    /// </param>
    /// <remarks>
    /// The process running this code must have audio group membership
    /// (sudo usermod -aG audio &lt;user&gt; + logout/login) to access ALSA devices.
    /// The sg/audio workaround only applies to subprocess calls.
    /// </remarks>
    public AlsaCapture(string deviceName = "hw:1,0")
    {
        DeviceName = deviceName;

        var ret = snd_pcm_open(out _pcm, deviceName, StreamCapture, 0);
        if (ret < 0)
        {
            var err = Marshal.PtrToStringAnsi(snd_strerror(ret)) ?? $"error code {ret}";
            _pcm = IntPtr.Zero;
            throw new InvalidOperationException(
                $"ALSA: failed to open capture device '{deviceName}': {err}");
        }

        // Configure: 16KHz, mono, S16_LE, interleaved
        // latency (microseconds) = (buffer_size * 1_000_000) / rate
        // 50000 µs = 50ms buffer = 800 frames
        // softResample: 0 — RC08 natively supports 16KHz, no resampling needed
        ret = snd_pcm_set_params(_pcm, FormatS16Le, AccessInterleaved,
            Channels, SampleRate, softResample: 0, latency: 50000);
        if (ret < 0)
        {
            var err = Marshal.PtrToStringAnsi(snd_strerror(ret)) ?? $"error code {ret}";
            snd_pcm_close(_pcm);
            _pcm = IntPtr.Zero;
            throw new InvalidOperationException(
                $"ALSA: failed to set params on '{deviceName}': {err}");
        }
    }

    /// <summary>
    /// Read a specified duration of audio and return PCM data.
    /// </summary>
    /// <param name="milliseconds">Amount of audio to capture (10-5000ms).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Byte array of S16_LE PCM samples.</returns>
    public async Task<byte[]> ReadAsync(int milliseconds, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (milliseconds < 10)
            throw new ArgumentOutOfRangeException(nameof(milliseconds),
                "Minimum read duration is 10ms.");
        if (milliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(milliseconds),
                "Maximum read duration is 5000ms.");

        // Calculate frames needed
        // 16000 samples/sec ÷ 1000 ms/sec * ms = frames
        var framesNeeded = milliseconds * SampleRate / 1000;
        var bufferSize = framesNeeded * FrameSize;

        // Allocate unmanaged buffer
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var framesRead = 0;
            while (framesRead < framesNeeded)
            {
                ct.ThrowIfCancellationRequested();

                var remaining = framesNeeded - framesRead;
                var ret = snd_pcm_readi(_pcm,
                    IntPtr.Add(buffer, framesRead * FrameSize),
                    remaining);

                if (ret < 0)
                {
                    // Handle xrun (buffer overrun) and other recoverable errors
                    if (ret == Epipe)
                    {
                        var recovered = snd_pcm_recover(_pcm, ret, silent: 1);
                        if (recovered < 0)
                        {
                            var err = Marshal.PtrToStringAnsi(snd_strerror(recovered))
                                      ?? $"error code {recovered}";
                            throw new InvalidOperationException(
                                $"ALSA: unrecoverable error on '{DeviceName}': {err}");
                        }
                        continue; // Retry read
                    }

                    var errStr = Marshal.PtrToStringAnsi(snd_strerror(ret)) ?? $"error code {ret}";
                    throw new InvalidOperationException(
                        $"ALSA: read error on '{DeviceName}': {errStr}");
                }

                framesRead += ret;
            }

            // Copy to managed array
            var result = new byte[bufferSize];
            Marshal.Copy(buffer, result, 0, bufferSize);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Read audio until silence is detected or timeout reached.
    /// Simple energy-based VAD: stops when RMS < threshold for minimum silence duration.
    /// </summary>
    /// <param name="maxDurationMs">Maximum recording duration.</param>
    /// <param name="silenceThresholdMs">Ms of silence before stopping.</param>
    /// <param name="silenceRmsThreshold">RMS threshold for silence (0.0-1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Byte array of S16_LE PCM samples.</returns>
    public async Task<byte[]> ReadUntilSilenceAsync(
        int maxDurationMs = 5000,
        int silenceThresholdMs = 800,
        double silenceRmsThreshold = 0.02,
        CancellationToken ct = default)
    {
        const int chunkMs = 50; // Evaluate VAD every 50ms
        var chunkFrames = chunkMs * SampleRate / 1000;
        var chunkBytes = chunkFrames * FrameSize;
        var silenceFramesNeeded = silenceThresholdMs / chunkMs;

        var allAudio = new System.IO.MemoryStream();
        var silenceFrames = 0;
        var totalMs = 0;

        try
        {
            while (totalMs < maxDurationMs)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = await ReadAsync(chunkMs, ct);
                allAudio.Write(chunk, 0, chunk.Length);
                totalMs += chunkMs;

                // Simple RMS energy detection
                var rms = ComputeRms(chunk);
                if (rms < silenceRmsThreshold)
                {
                    silenceFrames++;
                    if (silenceFrames >= silenceFramesNeeded)
                    {
                        // Trim trailing silence from the buffer
                        var trimBytes = silenceFrames * chunkBytes;
                        var result = new byte[allAudio.Length - trimBytes];
                        allAudio.Position = 0;
                        _ = allAudio.Read(result, 0, result.Length);
                        return result;
                    }
                }
                else
                {
                    silenceFrames = 0;
                }
            }

            return allAudio.ToArray();
        }
        finally
        {
            allAudio.Dispose();
        }
    }

    /// <summary>
    /// Compute RMS (root mean square) of a S16_LE buffer as a 0.0-1.0 value.
    /// </summary>
    private static double ComputeRms(byte[] buffer)
    {
        var sampleCount = buffer.Length / 2;
        if (sampleCount == 0) return 0;

        double sumSquares = 0;
        for (var i = 0; i < buffer.Length; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += sample * (double)sample;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        // Normalize: max S16_LE value is 32767
        return rms / 32767.0;
    }

    /// <summary>
    /// Drop any pending audio data and prepare for fresh capture.
    /// Useful after a pause to discard stale buffer contents.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        snd_pcm_drop(_pcm);
        snd_pcm_prepare(_pcm);
    }

    /// <summary>
    /// Enumerate available ALSA capture devices (requires arecord).
    /// </summary>
    public static async Task<List<string>> ListDevicesAsync()
    {
        var devices = new List<string>();
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "arecord",
                Arguments = "-l",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return devices;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Parse output like: "card 1: RC08 [USB Webcam RC08], device 0: USB Audio [USB Audio]"
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("card ", StringComparison.Ordinal))
                {
                    devices.Add(trimmed);
                }
            }
        }
        catch { /* arecord not available — return empty list */ }

        return devices;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_pcm != IntPtr.Zero)
            {
                snd_pcm_drop(_pcm);
                snd_pcm_close(_pcm);
                _pcm = IntPtr.Zero;
            }
        }
    }
}
