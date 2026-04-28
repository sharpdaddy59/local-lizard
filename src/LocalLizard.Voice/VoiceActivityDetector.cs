using System.Runtime.InteropServices;

namespace LocalLizard.Voice;

/// <summary>
/// Detects voice activity in PCM audio buffers.
/// Implementations should be stateless or cheap to call per-chunk.
/// </summary>
public interface IVoiceActivityDetector
{
    /// <summary>
    /// Analyze a buffer of S16_LE PCM samples and return true if voice is present.
    /// </summary>
    bool IsVoice(ReadOnlySpan<short> samples);

    /// <summary>
    /// Analyze raw PCM bytes (S16_LE). Default implementation casts to <see cref="short"/> and delegates.
    /// </summary>
    bool IsVoice(ReadOnlySpan<byte> pcmBytes) => IsVoice(MemoryMarshal.Cast<byte, short>(pcmBytes));

    /// <summary>Current RMS threshold in dBFS (-inf to 0).</summary>
    float ThresholdDb { get; set; }
}

/// <summary>
/// Energy-based VAD using RMS in dBFS.
/// Simple, fast (~5µs per 50ms chunk at 16KHz), good enough for quiet rooms.
/// </summary>
public sealed class EnergyVad : IVoiceActivityDetector
{
    private float _thresholdDb;

    /// <summary>
    /// Create energy-based VAD.
    /// </summary>
    /// <param name="thresholdDb">
    /// RMS threshold in dBFS. Default -35 dBFS ≈ quiet speech at 1m on RC08 at 50% gain.
    /// Higher (e.g., -30) = more sensitive to quiet sounds.
    /// Lower (e.g., -40) = less sensitive, rejects more background.
    /// </param>
    public EnergyVad(float thresholdDb = -35f)
    {
        _thresholdDb = thresholdDb;
    }

    public float ThresholdDb
    {
        get => _thresholdDb;
        set => _thresholdDb = value;
    }

    public bool IsVoice(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
            return false;

        // Compute RMS in dBFS
        // dBFS = 20 * log10(rms / 32768.0)
        double sumSquares = 0;
        foreach (var s in samples)
        {
            sumSquares += (double)s * s;
        }

        var rms = Math.Sqrt(sumSquares / samples.Length);
        var dB = 20.0 * Math.Log10(rms / 32768.0);

        // Voice if energy is above threshold
        return dB > _thresholdDb;
    }

    /// <summary>
    /// Convenience overload accepting raw PCM bytes (S16_LE).
    /// </summary>
    public bool IsVoice(ReadOnlySpan<byte> pcmBytes)
        => IsVoice(MemoryMarshal.Cast<byte, short>(pcmBytes));
}
