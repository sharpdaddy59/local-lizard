using System.Diagnostics;
using LocalLizard.Common;
using Whisper.net;

namespace LocalLizard.Voice;

/// <summary>
/// Always-listening wake word detection service.
/// Continuously captures audio from the microphone and uses Whisper.net
/// to detect a configurable wake phrase. When detected, records the
/// following audio as a voice command.
/// </summary>
public sealed class WakeWordService : IDisposable
{
    private readonly LizardConfig _config;
    private readonly WhisperFactory _factory;
    private readonly string _wakePhrase;
    private readonly int _sampleRate;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _silenceTimeout;

    private Process? _captureProcess;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;
    private bool _isListening;

    /// <summary>
    /// Fired when the wake word is detected and command recording begins.
    /// </summary>
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    /// <summary>
    /// Fired when a command has been recorded after wake word detection.
    /// </summary>
    public event EventHandler<CommandRecordedEventArgs>? CommandRecorded;

    /// <summary>
    /// Fired when an error occurs during listening.
    /// </summary>
    public event EventHandler<ErrorEventArgs>? ListenError;

    /// <summary>
    /// Whether the service is currently listening for the wake word.
    /// </summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// The wake phrase being listened for.
    /// </summary>
    public string WakePhrase => _wakePhrase;

    public WakeWordService(LizardConfig config, WhisperFactory factory)
    {
        _config = config;
        _factory = factory;
        _wakePhrase = config.WakePhrase;
        _sampleRate = 16000;
        _checkInterval = TimeSpan.FromSeconds(config.WakeWordCheckIntervalSec);
        _commandTimeout = TimeSpan.FromSeconds(config.WakeWordCommandTimeoutSec);
        _silenceTimeout = TimeSpan.FromSeconds(config.WakeWordSilenceTimeoutSec);
    }

    /// <summary>
    /// Start continuously listening for the wake word.
    /// </summary>
    public void StartListening(CancellationToken ct = default)
    {
        if (_isListening)
            throw new InvalidOperationException("Already listening");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isListening = true;
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

        Console.WriteLine($"[WakeWord] Listening for \"{_wakePhrase}\" (check every {_checkInterval.TotalSeconds}s)");
    }

    /// <summary>
    /// Stop listening for the wake word.
    /// </summary>
    public async Task StopListeningAsync()
    {
        if (!_isListening) return;

        _cts?.Cancel();
        _isListening = false;

        StopCaptureProcess();

        if (_listenTask != null)
        {
            try { await _listenTask; } catch (OperationCanceledException) { }
        }

        Console.WriteLine("[WakeWord] Stopped listening");
    }

    /// <summary>
    /// Main listening loop: capture audio in chunks, check for wake word, record command.
    /// </summary>
    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Capture a short audio chunk
                var chunkPath = Path.Combine(Path.GetTempPath(), $"lizard-wake-{Guid.NewGuid():N}.wav");
                await CaptureAudioChunkAsync(chunkPath, _checkInterval, ct);

                if (ct.IsCancellationRequested) break;

                // Check for wake word in the chunk
                var text = await TranscribeChunkAsync(chunkPath, ct);
                CleanupFile(chunkPath);

                if (ct.IsCancellationRequested) break;

                if (ContainsWakePhrase(text))
                {
                    Console.WriteLine($"[WakeWord] Detected! Transcription: \"{text}\"");

                    // Notify listeners that wake word was detected
                    WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                    {
                        Transcription = text,
                        Timestamp = DateTime.UtcNow,
                    });

                    // Record the actual command
                    var commandAudio = await RecordCommandAsync(ct);

                    if (commandAudio != null && !string.IsNullOrEmpty(commandAudio.AudioPath))
                    {
                        CommandRecorded?.Invoke(this, new CommandRecordedEventArgs
                        {
                            AudioPath = commandAudio.AudioPath,
                            Transcription = commandAudio.Transcription,
                            Timestamp = DateTime.UtcNow,
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WakeWord] Error: {ex.Message}");
                ListenError?.Invoke(this, new ErrorEventArgs(ex));

                // Brief pause before retrying to avoid tight error loops
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// Capture a short audio chunk using arecord (ALSA).
    /// </summary>
    private async Task CaptureAudioChunkAsync(string outputPath, TimeSpan duration, CancellationToken ct)
    {
        var durationSec = (int)Math.Ceiling(duration.TotalSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = "arecord",
            Arguments = $"-q -r {_sampleRate} -f S16_LE -c 1 -d {durationSec} \"{outputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("arecord not found. Install alsa-utils.");

        _captureProcess = proc;

        // Read stderr to prevent blocking
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        _captureProcess = null;
    }

    /// <summary>
    /// Transcribe a short audio chunk using Whisper.net.
    /// </summary>
    private async Task<string> TranscribeChunkAsync(string audioPath, CancellationToken ct)
    {
        if (!File.Exists(audioPath))
            return string.Empty;

        using var processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        await using var fileStream = File.OpenRead(audioPath);
        var text = new System.Text.StringBuilder();

        await foreach (var result in processor.ProcessAsync(fileStream, ct))
        {
            text.Append(result.Text);
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// Check if the transcription contains the wake phrase.
    /// Uses case-insensitive contains matching with some fuzziness for Whisper errors.
    /// </summary>
    private bool ContainsWakePhrase(string transcription)
    {
        if (string.IsNullOrWhiteSpace(transcription))
            return false;

        var lower = transcription.ToLowerInvariant();
        var wakeLower = _wakePhrase.ToLowerInvariant();

        // Direct match
        if (lower.Contains(wakeLower))
            return true;

        // Check each word of the wake phrase appears nearby
        var wakeWords = wakeLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wakeWords.Length > 1)
        {
            var transWords = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int found = 0;
            foreach (var word in transWords)
            {
                if (wakeWords.Contains(word))
                    found++;
            }
            // If most wake words are found, consider it a match
            if (found >= Math.Ceiling(wakeWords.Length * 0.7))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Record audio after wake word detection. Stops when silence is detected
    /// or the command timeout is reached.
    /// </summary>
    private async Task<CommandAudio?> RecordCommandAsync(CancellationToken ct)
    {
        Console.WriteLine("[WakeWord] Recording command...");

        var commandPath = Path.Combine(Path.GetTempPath(), $"lizard-cmd-{Guid.NewGuid():N}.wav");

        // Start recording with silence detection
        var psi = new ProcessStartInfo
        {
            FileName = "arecord",
            Arguments = $"-q -r {_sampleRate} -f S16_LE -c 1 -d {(int)_commandTimeout.TotalSeconds} \"{commandPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("arecord not found");

        _captureProcess = proc;

        // Start silence monitoring
        var silenceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var silenceTask = MonitorForSilenceAsync(commandPath, silenceCts.Token);

        // Wait for recording to finish (either timeout or silence)
        var recordTask = proc.WaitForExitAsync(ct);

        await Task.WhenAny(recordTask, silenceTask);

        // Clean up
        silenceCts.Cancel();
        try { proc.Kill(true); } catch { }

        _captureProcess = null;

        if (!File.Exists(commandPath))
            return null;

        // Transcribe the command
        var transcription = await TranscribeChunkAsync(commandPath, CancellationToken.None);

        Console.WriteLine($"[WakeWord] Command recorded: \"{transcription}\"");

        return new CommandAudio
        {
            AudioPath = commandPath,
            Transcription = transcription,
        };
    }

    /// <summary>
    /// Monitor audio file growth to detect silence (file stops growing).
    /// </summary>
    private async Task MonitorForSilenceAsync(string filePath, CancellationToken ct)
    {
        try
        {
            // Give initial delay for recording to start
            await Task.Delay(500, ct);

            long lastSize = 0;
            var silenceStart = DateTime.MinValue;
            var checkInterval = TimeSpan.FromMilliseconds(300);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(checkInterval, ct);

                if (!File.Exists(filePath))
                    continue;

                var currentSize = new FileInfo(filePath).Length;

                if (currentSize == lastSize)
                {
                    // No growth = silence
                    if (silenceStart == DateTime.MinValue)
                        silenceStart = DateTime.UtcNow;
                    else if (DateTime.UtcNow - silenceStart > _silenceTimeout)
                    {
                        Console.WriteLine("[WakeWord] Silence detected, stopping recording");
                        StopCaptureProcess();
                        return;
                    }
                }
                else
                {
                    // Audio is being produced, reset silence timer
                    silenceStart = DateTime.MinValue;
                }

                lastSize = currentSize;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopCaptureProcess()
    {
        try { _captureProcess?.Kill(true); } catch { }
        _captureProcess = null;
    }

    private static void CleanupFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        StopCaptureProcess();

        try { _listenTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        _cts?.Dispose();
    }
}

public class WakeWordDetectedEventArgs : EventArgs
{
    public string Transcription { get; init; } = "";
    public DateTime Timestamp { get; init; }
}

public class CommandRecordedEventArgs : EventArgs
{
    public string AudioPath { get; init; } = "";
    public string Transcription { get; init; } = "";
    public DateTime Timestamp { get; init; }
}

public class ErrorEventArgs : EventArgs
{
    public Exception Exception { get; }
    public ErrorEventArgs(Exception ex) => Exception = ex;
}

internal record CommandAudio
{
    public string AudioPath { get; init; } = "";
    public string Transcription { get; init; } = "";
}
