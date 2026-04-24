using System.Diagnostics;
using LocalLizard.Common;
using Whisper.net;
using Whisper.net.Ggml;

namespace LocalLizard.Voice;

/// <summary>
/// Speech-to-text service using Whisper.net (P/Invoke bindings for whisper.cpp).
/// Provides better performance and features than the process wrapper approach.
/// </summary>
public sealed class WhisperSTTService : IDisposable
{
    private readonly LizardConfig _config;
    private WhisperFactory? _factory;
    private bool _disposed;

    public WhisperSTTService(LizardConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Initializes the Whisper factory with the configured model.
    /// </summary>
    private async Task<WhisperFactory> GetOrCreateFactoryAsync(CancellationToken ct = default)
    {
        if (_factory != null)
            return _factory;

        // Ensure model exists
        await EnsureModelExistsAsync(ct);

        // Create factory from model file
        _factory = WhisperFactory.FromPath(_config.WhisperModelPath);
        return _factory;
    }

    /// <summary>
    /// Ensures the Whisper model file exists, downloading it if necessary.
    /// </summary>
    private async Task EnsureModelExistsAsync(CancellationToken ct = default)
    {
        if (File.Exists(_config.WhisperModelPath))
            return;

        Console.WriteLine($"Whisper model not found at {_config.WhisperModelPath}. Downloading...");
        
        // Create directory if it doesn't exist
        var modelDir = Path.GetDirectoryName(_config.WhisperModelPath);
        if (!string.IsNullOrEmpty(modelDir) && !Directory.Exists(modelDir))
            Directory.CreateDirectory(modelDir);

        // Download base model (smallest for testing)
        using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base);
        using var fileStream = File.OpenWrite(_config.WhisperModelPath);
        await modelStream.CopyToAsync(fileStream, ct);
        
        Console.WriteLine($"Downloaded Whisper model to {_config.WhisperModelPath}");
    }

    /// <summary>
    /// Transcribes an audio file to text.
    /// Automatically resamples to 16KHz if needed (Whisper requirement).
    /// </summary>
    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException($"Audio file not found: {audioFilePath}");

        // Resample to 16KHz WAV if needed
        var resampledPath = await Ensure16KhzWavAsync(audioFilePath, ct);

        try
        {
            using var fileStream = File.OpenRead(resampledPath);
            return await TranscribeAsync(fileStream, ct);
        }
        finally
        {
            // Clean up temp file if we created one
            if (resampledPath != audioFilePath)
            {
                try { File.Delete(resampledPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Ensures audio is 16KHz mono WAV. Resamples via ffmpeg if needed.
    /// Returns original path if already correct format, temp path otherwise.
    /// </summary>
    private async Task<string> Ensure16KhzWavAsync(string inputPath, CancellationToken ct = default)
    {
        // Quick check: if file looks like a raw PCM WAV, try it directly first
        // ffmpeg is the reliable path for format conversion
        var tempPath = Path.GetTempFileName() + ".wav";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -sample_fmt s16 \"{tempPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg");
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            var error = await proc.StandardError.ReadToEndAsync(ct);
            // If ffmpeg fails, try original file directly
            try { File.Delete(tempPath); } catch { }
            return inputPath;
        }

        return tempPath;
    }

    /// <summary>
    /// Transcribes audio from a stream to text.
    /// </summary>
    public async Task<string> TranscribeAsync(Stream audioStream, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(ct);
        
        // Create processor with configuration
        var builder = factory.CreateBuilder()
            .WithLanguage(_config.WhisperLanguage);
        
        // Configure threads for CPU inference
        if (!_config.WhisperUseGpu)
            builder.WithThreads(_config.WhisperThreads);
        
        using var processor = builder.Build();

        // Process audio and collect results
        var fullText = new System.Text.StringBuilder();
        await foreach (var result in processor.ProcessAsync(audioStream, ct))
        {
            fullText.Append(result.Text);
        }

        return fullText.ToString().Trim();
    }

    /// <summary>
    /// Transcribes an audio file with timestamps for each segment.
    /// </summary>
    public async Task<List<TranscriptionSegment>> TranscribeWithTimestampsAsync(
        string audioFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException($"Audio file not found: {audioFilePath}");

        using var fileStream = File.OpenRead(audioFilePath);
        return await TranscribeWithTimestampsAsync(fileStream, ct);
    }

    /// <summary>
    /// Transcribes audio from a stream with timestamps for each segment.
    /// </summary>
    public async Task<List<TranscriptionSegment>> TranscribeWithTimestampsAsync(
        Stream audioStream, CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(ct);
        
        // Create processor with configuration
        var builder = factory.CreateBuilder()
            .WithLanguage(_config.WhisperLanguage);
        
        // Configure threads for CPU inference
        if (!_config.WhisperUseGpu)
            builder.WithThreads(_config.WhisperThreads);
        
        using var processor = builder.Build();

        // Process audio and collect segments
        var segments = new List<TranscriptionSegment>();
        await foreach (var result in processor.ProcessAsync(audioStream, ct))
        {
            segments.Add(new TranscriptionSegment
            {
                Start = result.Start,
                End = result.End,
                Text = result.Text.Trim()
            });
        }

        return segments;
    }

    /// <summary>
    /// Gets information about the loaded Whisper model.
    /// </summary>
    public async Task<ModelInfo> GetModelInfoAsync(CancellationToken ct = default)
    {
        var factory = await GetOrCreateFactoryAsync(ct);
        
        // Note: Whisper.net doesn't expose model info directly in the public API.
        // We'll return basic info based on file.
        var fileInfo = new FileInfo(_config.WhisperModelPath);
        
        return new ModelInfo
        {
            Path = _config.WhisperModelPath,
            SizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _factory?.Dispose();
            _factory = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a transcription segment with timestamps.
/// </summary>
public class TranscriptionSegment
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = string.Empty;

    public override string ToString() => $"[{Start:hh\\:mm\\:ss\\.fff} -> {End:hh\\:mm\\:ss\\.fff}] {Text}";
}

/// <summary>
/// Information about the loaded Whisper model.
/// </summary>
public class ModelInfo
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }

    public string SizeFormatted => SizeBytes switch
    {
        >= 1_000_000_000 => $"{(SizeBytes / 1_000_000_000.0):F2} GB",
        >= 1_000_000 => $"{(SizeBytes / 1_000_000.0):F2} MB",
        >= 1_000 => $"{(SizeBytes / 1_000.0):F2} KB",
        _ => $"{SizeBytes} B"
    };
}