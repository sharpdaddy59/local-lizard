using System.Diagnostics;
using LocalLizard.Common;

namespace LocalLizard.Voice;

/// <summary>
/// Text-to-speech service using Piper (process wrapper).
/// Provides TTS synthesis with configurable voice models and parameters.
/// </summary>
public sealed class PiperTTSService : IDisposable
{
    private readonly LizardConfig _config;
    private Process? _piperProcess;
    private bool _disposed;
    private readonly object _processLock = new();

    public PiperTTSService(LizardConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Validates that Piper executable and model exist.
    /// </summary>
    public async Task<bool> ValidateInstallationAsync(CancellationToken ct = default)
    {
        // Check if Piper executable exists
        if (!File.Exists(_config.PiperPath))
        {
            Console.WriteLine($"Piper executable not found at: {_config.PiperPath}");
            return false;
        }

        // Check if model file exists
        if (!File.Exists(_config.PiperModel))
        {
            Console.WriteLine($"Piper model not found at: {_config.PiperModel}");
            return false;
        }

        // Try to run Piper with --help to verify it works
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.PiperPath,
                Arguments = "--help",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to validate Piper installation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Synthesizes text to a WAV file.
    /// </summary>
    public async Task<string> SynthesizeToFileAsync(string text, string outputPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty", nameof(text));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var psi = new ProcessStartInfo
        {
            FileName = _config.PiperPath,
            Arguments = BuildPiperArguments(outputPath),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new PiperException($"Failed to start Piper process");
        
        try
        {
            // Write text to stdin
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();

            // Read any output (Piper might write to stdout even with -f)
            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errorTask = proc.StandardError.ReadToEndAsync(ct);
            
            // Wait for process to complete with timeout
            var processTask = proc.WaitForExitAsync(ct);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), ct);
            
            var completedTask = await Task.WhenAny(processTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                proc.Kill(true);
                throw new TimeoutException("Piper process timeout after 30 seconds");
            }

            await processTask; // Ensure process has exited
            
            // Check for errors
            var errorOutput = await errorTask;
            if (proc.ExitCode != 0)
            {
                throw new PiperException($"Piper failed with exit code {proc.ExitCode}: {errorOutput}");
            }

            // Verify output file was created
            if (!File.Exists(outputPath))
            {
                throw new PiperException($"Output file was not created: {outputPath}");
            }

            return outputPath;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            proc.Kill(true);
            throw;
        }
    }

    /// <summary>
    /// Synthesizes text to a WAV byte array.
    /// </summary>
    public async Task<byte[]> SynthesizeToMemoryAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty", nameof(text));

        // Use a temporary file
        var tempFile = Path.GetTempFileName() + ".wav";
        try
        {
            await SynthesizeToFileAsync(text, tempFile, ct);
            return await File.ReadAllBytesAsync(tempFile, ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Starts a streaming synthesis session for real-time audio output.
    /// </summary>
    public async Task<PiperStreamSession> StartStreamingSessionAsync(CancellationToken ct = default)
    {
        lock (_processLock)
        {
            if (_piperProcess != null && !_piperProcess.HasExited)
                throw new InvalidOperationException("A streaming session is already active");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _config.PiperPath,
            Arguments = BuildPiperArguments(null), // No output file for streaming
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var proc = Process.Start(psi) ?? throw new PiperException($"Failed to start Piper process");
        
        lock (_processLock)
        {
            _piperProcess = proc;
        }

        return new PiperStreamSession(proc, ct);
    }

    /// <summary>
    /// Builds Piper command-line arguments based on configuration.
    /// </summary>
    private string BuildPiperArguments(string? outputPath)
    {
        var args = new List<string>
        {
            $"-m \"{_config.PiperModel}\""
        };

        if (!string.IsNullOrEmpty(outputPath))
        {
            args.Add($"-f \"{outputPath}\"");
        }
        else
        {
            // For streaming, output raw WAV to stdout
            args.Add("--output-raw");
        }

        // Add additional Piper parameters here as needed
        // Example: args.Add("--length-scale 1.0");
        // Example: args.Add("--noise-scale 0.667");
        // Example: args.Add("--noise-w 0.8");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Gets information about the configured Piper model.
    /// </summary>
    public async Task<PiperModelInfo> GetModelInfoAsync(CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(_config.PiperModel);
        
        return new PiperModelInfo
        {
            Path = _config.PiperModel,
            SizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue,
            ExecutablePath = _config.PiperPath,
            ExecutableExists = File.Exists(_config.PiperPath)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_processLock)
            {
                if (_piperProcess != null && !_piperProcess.HasExited)
                {
                    try
                    {
                        _piperProcess.Kill(true);
                    }
                    catch
                    {
                        // Ignore errors during disposal
                    }
                    _piperProcess.Dispose();
                }
                _piperProcess = null;
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a streaming TTS session for real-time audio output.
/// </summary>
public sealed class PiperStreamSession : IDisposable
{
    private readonly Process _process;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    internal PiperStreamSession(Process process, CancellationToken cancellationToken)
    {
        _process = process;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Synthesizes text and returns the audio bytes.
    /// </summary>
    public async Task<byte[]> SynthesizeChunkAsync(string text)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PiperStreamSession));

        if (_process.HasExited)
            throw new InvalidOperationException("Piper process has exited");

        // Write text to stdin
        await _process.StandardInput.WriteAsync(text);
        await _process.StandardInput.WriteAsync('\n'); // Newline triggers processing
        await _process.StandardInput.FlushAsync();

        // Read raw WAV audio from stdout
        // Note: This is simplified - in reality, Piper's raw output needs proper parsing
        using var memoryStream = new MemoryStream();
        var buffer = new byte[4096];
        
        // Read for a short time to get audio chunk
        var readTask = _process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length, _cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), _cancellationToken);
        
        var completedTask = await Task.WhenAny(readTask, timeoutTask);
        if (completedTask == timeoutTask)
            throw new TimeoutException("Timeout reading audio from Piper");

        var bytesRead = await readTask;
        if (bytesRead > 0)
        {
            memoryStream.Write(buffer, 0, bytesRead);
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Closes the streaming session.
    /// </summary>
    public void Close()
    {
        if (!_disposed && !_process.HasExited)
        {
            _process.StandardInput.Close();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _process.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Information about a Piper voice model.
/// </summary>
public class PiperModelInfo
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public bool ExecutableExists { get; set; }

    public string SizeFormatted => SizeBytes switch
    {
        >= 1_000_000_000 => $"{(SizeBytes / 1_000_000_000.0):F2} GB",
        >= 1_000_000 => $"{(SizeBytes / 1_000_000.0):F2} MB",
        >= 1_000 => $"{(SizeBytes / 1_000.0):F2} KB",
        _ => $"{SizeBytes} B"
    };
}

/// <summary>
/// Exception thrown by PiperTTSService.
/// </summary>
public class PiperException : Exception
{
    public PiperException(string message) : base(message) { }
    public PiperException(string message, Exception innerException) : base(message, innerException) { }
}