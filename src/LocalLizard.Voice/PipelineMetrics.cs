using System.Diagnostics;

namespace LocalLizard.Voice;

/// <summary>
/// Diagnostic timing collector for voice pipeline stages.
/// Thread-safe: uses ConcurrentQueue internally so the same instance
/// can be shared across concurrent pipelines (Telegram + physical mic).
/// </summary>
public sealed class PipelineMetrics
{
    private readonly string _source;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly Dictionary<string, long> _timings = new();
    private readonly object _lock = new();

    // Track per-stage warm/cold state
    private static int _sttCallCount;
    private static int _ttsCallCount;
    private static int _vadCallCount;

    /// <summary>
    /// Global flag to enable/disable metrics Dump output.
    /// Set to false for production use to avoid console noise.
    /// Individual timing collection is still active for programmatic access.
    /// </summary>
    public static bool DumpEnabled { get; set; } = true;

    public PipelineMetrics(string source = "default")
    {
        _source = source;
    }

    /// <summary>
    /// Record a duration for a pipeline stage.
    /// Use with Stopwatch from the calling code — each stage manages its own timer
    /// so the wall clock total may differ from the sum of recorded stages.
    /// </summary>
    public void Record(string stageName, long elapsedMs)
    {
        lock (_lock)
        {
            _timings[stageName] = elapsedMs;
        }
    }

    /// <summary>
    /// Get the total wall-clock time since this metrics instance was created.
    /// </summary>
    public long TotalMs => _wall.ElapsedMilliseconds;

    /// <summary>
    /// Print a formatted timing summary to Console (only if DumpEnabled).
    /// Call this at the end of a pipeline cycle to see the breakdown.
    /// </summary>
    public void Dump()
    {
        if (!DumpEnabled) return;
        lock (_lock)
        {
            if (_timings.Count == 0)
            {
                Console.WriteLine($"[{_source}] (no timings recorded)");
                return;
            }

            var total = _wall.ElapsedMilliseconds;
            Console.WriteLine($"[{_source}] {new string('▔', 50)}");
            foreach (var (stage, ms) in _timings)
            {
                var pct = total > 0 ? (ms * 100.0 / total) : 0.0;
                var bar = new string('█', (int)(ms * 40 / Math.Max(total, 1)));
                Console.WriteLine($"  {stage,-22} {ms,6}ms ({pct,4:F0}%) {bar}");
            }
            Console.WriteLine($"  {new string('▁', 50)}");
            Console.WriteLine($"  {"Total",-22} {total,6}ms");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Reset for the next pipeline cycle.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _timings.Clear();
            _wall.Restart();
        }
    }

    // ---- Static helpers for warm/cold tracking ----

    public static bool IsSttCold => Interlocked.CompareExchange(ref _sttCallCount, 0, 0) == 0;
    public static int SttCallCount => Interlocked.CompareExchange(ref _sttCallCount, 0, 0);
    public static void IncrementSttCalls() => Interlocked.Increment(ref _sttCallCount);

    public static bool IsTtsCold => Interlocked.CompareExchange(ref _ttsCallCount, 0, 0) == 0;
    public static int TtsCallCount => Interlocked.CompareExchange(ref _ttsCallCount, 0, 0);
    public static void IncrementTtsCalls() => Interlocked.Increment(ref _ttsCallCount);

    public static bool IsVadCold => Interlocked.CompareExchange(ref _vadCallCount, 0, 0) == 0;
    public static int VadCallCount => Interlocked.CompareExchange(ref _vadCallCount, 0, 0);
    public static void IncrementVadCalls() => Interlocked.Increment(ref _vadCallCount);
}
