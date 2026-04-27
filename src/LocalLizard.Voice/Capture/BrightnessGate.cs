using System.Diagnostics;

namespace LocalLizard.Voice.Capture;

/// <summary>
/// Uses the webcam's average brightness as a listen on/off gate.
/// When the physical camera cover is closed, brightness drops to near-zero.
/// Open cover = bot is allowed to listen. Closed cover = privacy mode.
///
/// Designed to minimize LED activation: grabs one frame per check,
/// not continuously. Caller controls check frequency.
/// </summary>
public sealed class BrightnessGate : IDisposable
{
    private readonly string _videoDevice;
    private readonly int _threshold;
    private readonly string _ffmpegPath;
    private bool _disposed;

    /// <summary>
    /// Create a brightness gate using the specified video device.
    /// </summary>
    /// <param name="videoDevice">V4L2 device path (e.g., "/dev/video0").</param>
    /// <param name="threshold">
    /// Average brightness threshold (0-255). Below = cover closed (don't listen).
    /// Default 10 — well above the 0.7 closed-cover reading, well below 65.9 open.
    /// </param>
    /// <param name="ffmpegPath">Path to ffmpeg binary. Default "ffmpeg" (uses PATH).</param>
    public BrightnessGate(string videoDevice = "/dev/video0", int threshold = 10, string ffmpegPath = "ffmpeg")
    {
        _videoDevice = videoDevice;
        _threshold = threshold;
        _ffmpegPath = ffmpegPath;
    }

    /// <summary>
    /// Check whether the camera cover is open (brightness above threshold).
    /// Grabs a single low-res JPEG frame via ffmpeg and computes average brightness.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cover is open (listening allowed), false if closed.</returns>
    public async Task<bool> IsOpenAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var brightness = await ComputeBrightnessAsync(ct);
            return brightness >= _threshold;
        }
        catch (Exception ex)
        {
            // If camera check fails, default to listening (fail-open).
            // Don't block voice pipeline because camera is unplugged.
            Console.WriteLine($"[BrightnessGate] Camera check failed: {ex.Message}");
            Console.WriteLine("[BrightnessGate] Defaulting to open (listening enabled)");
            return true;
        }
    }

    /// <summary>
    /// Compute average brightness from a single camera frame.
    /// Uses ffmpeg to grab one JPEG at minimal resolution, then analyzes pixel data.
    /// </summary>
    private async Task<double> ComputeBrightnessAsync(CancellationToken ct)
    {
        // Grab a single frame at lowest resolution as raw grayscale
        // -frames:v 1 = one frame only
        // -s 32x24 = tiny resolution (fast, enough for brightness)
        // -pix_fmt gray = single channel grayscale
        // -f rawvideo = raw output to stdout
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-f v4l2 -i {_videoDevice} -frames:v 1 -s 32x24 -pix_fmt gray -f rawvideo pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        // Read raw grayscale pixels from stdout
        using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await proc.WaitForExitAsync(ct);

        var pixels = ms.ToArray();
        if (pixels.Length == 0)
            throw new InvalidOperationException("No frame data received from camera");

        // Compute average brightness
        double sum = 0;
        for (var i = 0; i < pixels.Length; i++)
            sum += pixels[i];

        return sum / pixels.Length;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
