using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalLizard.Common;
using LocalLizard.Voice.Capture;

namespace LocalLizard.Voice;

/// <summary>
/// Voice pipeline supporting both whisper.cpp (STT) and piper (TTS).
/// Uses Whisper.net for STT when available, falls back to process wrapper.
/// Uses PiperTTSService for TTS with fallback to basic process wrapper.
/// </summary>
public sealed class VoicePipeline : IDisposable
{
    private readonly LizardConfig _config;
    private WhisperSTTService? _whisperService;
    private PiperTTSService? _piperService;
    private readonly Lazy<AlsaCapture> _alsaCapture;
    private readonly Lazy<BrightnessGate> _brightnessGate;
    private bool _useWhisperNet = true;
    private bool _usePiperService = true;
    private int _whisperNetFailCount;
    private bool _disposed;

    public VoicePipeline(LizardConfig config)
    {
        _config = config;
        _alsaCapture = new Lazy<AlsaCapture>(() => new AlsaCapture(config.AlsaDevice), LazyThreadSafetyMode.ExecutionAndPublication);
        _brightnessGate = new Lazy<BrightnessGate>(() => new BrightnessGate(config.CameraDevice, config.CameraBrightnessThreshold), LazyThreadSafetyMode.ExecutionAndPublication);
        InitializeWhisperService();
        InitializePiperService();
    }

    /// <summary>
    /// Initializes the Whisper service, falling back to process wrapper if Whisper.net fails.
    /// </summary>
    private void InitializeWhisperService()
    {
        try
        {
            _whisperService = new WhisperSTTService(_config);
            _useWhisperNet = true;
            Console.WriteLine("Using Whisper.net for speech-to-text");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize Whisper.net: {ex.Message}");
            Console.WriteLine("Falling back to process wrapper for whisper.cpp");
            _useWhisperNet = false;
            _whisperService?.Dispose();
            _whisperService = null;
        }
    }

    /// <summary>
    /// Initializes the Piper service, falling back to basic process wrapper if PiperTTSService fails.
    /// </summary>
    private void InitializePiperService()
    {
        try
        {
            _piperService = new PiperTTSService(_config);
            _usePiperService = true;
            Console.WriteLine("Using PiperTTSService for text-to-speech");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize PiperTTSService: {ex.Message}");
            Console.WriteLine("Falling back to basic process wrapper for piper");
            _usePiperService = false;
            _piperService?.Dispose();
            _piperService = null;
        }
    }

    /// <summary>
    /// Transcribe a WAV file to text using whisper.cpp.
    /// Uses Whisper.net when available, falls back to process wrapper.
    /// </summary>
    public async Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (_useWhisperNet && _whisperService != null)
        {
            try
            {
                return await _whisperService.TranscribeAsync(wavPath, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Whisper.net failed: {ex.Message}");
                Console.WriteLine("Falling back to process wrapper");
                _whisperNetFailCount++;
                // Re-enable Whisper.net after 3 consecutive failures reset
                // (a restart or different input might succeed)
                if (_whisperNetFailCount >= 3)
                {
                    Console.WriteLine("Whisper.net disabled after 3 failures. Restart pipeline to re-enable.");
                    _useWhisperNet = false;
                }
            }
        }

        // Fallback to process wrapper
        return await TranscribeWithProcessWrapperAsync(wavPath, ct);
    }

    /// <summary>
    /// Transcribe with timestamps using Whisper.net.
    /// Only available when Whisper.net is working.
    /// </summary>
    public async Task<List<TranscriptionSegment>> TranscribeWithTimestampsAsync(
        string wavPath, CancellationToken ct = default)
    {
        if (_whisperService == null)
            throw new InvalidOperationException("Whisper.net service not available");

        return await _whisperService.TranscribeWithTimestampsAsync(wavPath, ct);
    }

    /// <summary>
    /// Get information about the loaded Whisper model.
    /// Only available when Whisper.net is working.
    /// </summary>
    public async Task<ModelInfo?> GetWhisperModelInfoAsync(CancellationToken ct = default)
    {
        if (_whisperService == null)
            return null;

        return await _whisperService.GetModelInfoAsync(ct);
    }

    /// <summary>
    /// Original process wrapper implementation for whisper.cpp.
    /// </summary>
    private async Task<string> TranscribeWithProcessWrapperAsync(string wavPath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _config.WhisperPath,
            Arguments = $"-m base.en -f \"{wavPath}\" --no-timestamps -otxt",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start whisper");
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return output.Trim();
    }

    /// <summary>
    /// Synthesize text to a WAV file using piper.
    /// Uses PiperTTSService when available, falls back to basic process wrapper.
    /// </summary>
    public async Task<string> SynthesizeAsync(string text, string outputPath, CancellationToken ct = default)
    {
        if (_usePiperService && _piperService != null)
        {
            try
            {
                return await _piperService.SynthesizeToFileAsync(text, outputPath, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PiperTTSService failed: {ex.Message}");
                Console.WriteLine("Falling back to basic process wrapper");
                _usePiperService = false;
            }
        }

        // Fallback to basic process wrapper
        return await SynthesizeWithProcessWrapperAsync(text, outputPath, ct);
    }

    /// <summary>
    /// Synthesize text to a WAV byte array using piper.
    /// </summary>
    public async Task<byte[]> SynthesizeToMemoryAsync(string text, CancellationToken ct = default)
    {
        if (_piperService == null)
            throw new InvalidOperationException("Piper service not available");

        return await _piperService.SynthesizeToMemoryAsync(text, ct);
    }

    /// <summary>
    /// Starts a streaming TTS session for real-time audio output.
    /// </summary>
    public async Task<PiperStreamSession> StartStreamingSessionAsync(CancellationToken ct = default)
    {
        if (_piperService == null)
            throw new InvalidOperationException("Piper service not available");

        return await _piperService.StartStreamingSessionAsync(ct);
    }

    /// <summary>
    /// Validates Piper installation.
    /// </summary>
    public async Task<bool> ValidatePiperInstallationAsync(CancellationToken ct = default)
    {
        if (_piperService == null)
            return false;

        return await _piperService.ValidateInstallationAsync(ct);
    }

    /// <summary>
    /// Gets information about the configured Piper model.
    /// </summary>
    public async Task<PiperModelInfo?> GetPiperModelInfoAsync(CancellationToken ct = default)
    {
        if (_piperService == null)
            return null;

        return await _piperService.GetModelInfoAsync(ct);
    }

    /// <summary>
    /// Capture audio from the physical mic (via ALSA) and transcribe to text.
    /// Uses energy-based VAD to detect speech boundaries.
    /// </summary>
    /// <param name="ct">Cancellation token (stops capture immediately).</param>
    /// <returns>Transcribed text, or empty string if nothing detected.</returns>
    public async Task<string> CaptureAndTranscribeAsync(CancellationToken ct = default)
    {
        var alsa = _alsaCapture.Value;

        // Capture raw PCM with silence-based endpointing
        var pcmData = await alsa.ReadUntilSilenceAsync(
            maxDurationMs: _config.CaptureMaxDurationMs,
            silenceThresholdMs: _config.CaptureSilenceThresholdMs,
            silenceRmsThreshold: _config.CaptureSilenceRms,
            ct: ct);

        if (pcmData.Length == 0)
            return string.Empty;

        // Wrap raw PCM in WAV header for Whisper
        using var wavStream = PcmToWavStream(pcmData, AlsaCapture.SampleRate, AlsaCapture.Channels);

        // Transcribe using Whisper.net (with process wrapper fallback)
        if (_useWhisperNet && _whisperService != null)
        {
            try
            {
                return await _whisperService.TranscribeAsync(wavStream, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Whisper.net failed during capture: {ex.Message}");
                Console.WriteLine("Falling back to process wrapper");
                _whisperNetFailCount++;
                if (_whisperNetFailCount >= 3)
                    _useWhisperNet = false;
            }
        }

        // Fallback: write to temp WAV via random filename (no pre-creation)
        var tempPath = Path.Combine(Path.GetTempPath(), $"locallizard-{Path.GetRandomFileName()}.wav");
        try
        {
            await File.WriteAllBytesAsync(tempPath, wavStream.ToArray(), ct);
            return await TranscribeWithProcessWrapperAsync(tempPath, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Start a continuous listening loop using the physical mic.
    /// Uses the camera brightness gate as a privacy/listen toggle:
    /// cover open = listening, cover closed = privacy mode (mic ignored).
    ///
    /// The brightness check runs infrequently (configurable) to avoid
    /// blinking the webcam LED. Between checks, the previous state is assumed.
    /// </summary>
    /// <param name="onTranscribed">
    /// Callback invoked with transcribed text when speech is detected.
    /// Return true to continue listening, false to stop the loop.
    /// </param>
    /// <param name="ct">Cancellation token to stop the loop.</param>
    public async Task StartListeningLoopAsync(
        Func<string, Task<bool>> onTranscribed,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gate = _brightnessGate.Value;
        var checkInterval = TimeSpan.FromSeconds(_config.CameraCheckIntervalSec);
        var minAudioBytes = _config.CaptureMinAudioMs * AlsaCapture.FrameSize * AlsaCapture.SampleRate / 1000;

        var lastCheck = DateTime.MinValue;
        var listening = true; // Assume open until first check
        var gateCheckCount = 0;

        Console.WriteLine("[ListeningLoop] Starting. Camera cover = privacy toggle.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check brightness gate periodically (not every cycle)
                var now = DateTime.UtcNow;
                if (now - lastCheck >= checkInterval)
                {
                    listening = await gate.IsOpenAsync(ct);
                    lastCheck = now;
                    gateCheckCount++;

                    if (!listening)
                    {
                        // Cover closed — sleep and re-check
                        await Task.Delay(500, ct);
                        continue;
                    }
                }

                if (!listening)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                // Capture from mic with silence-based endpointing
                var text = await CaptureAndTranscribeAsync(ct);

                // Filter: reject empty or very short transcriptions (noise)
                if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                    continue;

                Console.WriteLine($"[ListeningLoop] Heard: \"{text}\"");

                // Invoke callback
                var shouldContinue = await onTranscribed(text);
                if (!shouldContinue)
                {
                    Console.WriteLine("[ListeningLoop] Callback requested stop.");
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ListeningLoop] Error: {ex.Message}");
                // Brief pause before retrying to avoid tight error loop
                await Task.Delay(1000, ct);
            }
        }

        Console.WriteLine($"[ListeningLoop] Stopped after {gateCheckCount} brightness checks.");
    }

    /// <summary>
    /// Convert raw S16_LE PCM bytes to a WAV stream with proper headers.
    /// Whisper expects a well-formed WAV file/stream.
    /// </summary>
    private static MemoryStream PcmToWavStream(byte[] pcmData, int sampleRate, int channels)
    {
        var bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = channels * bitsPerSample / 8;
        var dataSize = pcmData.Length;
        var fileSize = 36 + dataSize;

        var ms = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM format
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(pcmData);

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Synthesize text to speech and play it through the default audio output (aplay).
    /// Pipes Piper output directly to aplay for zero-temp-file playback.
    /// </summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Synthesize to memory via Piper
        byte[] wavData;
        if (_usePiperService && _piperService != null)
        {
            try
            {
                wavData = await _piperService.SynthesizeToMemoryAsync(text, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PiperTTSService failed: {ex.Message}");
                Console.WriteLine("Falling back to process wrapper for speech");
                _usePiperService = false;
                wavData = await SynthesizeToMemoryWithWrapperAsync(text, ct);
            }
        }
        else
        {
            wavData = await SynthesizeToMemoryWithWrapperAsync(text, ct);
        }

        // Play via aplay (pipe WAV data directly)
        await PlayAudioAsync(wavData, ct);
    }

    /// <summary>
    /// Start a full conversation loop: listen → route/respond → speak → repeat.
    /// Uses the camera brightness gate as privacy toggle.
    /// </summary>
    /// <param name="onHeard">
    /// Async function that receives transcribed text and returns the response to speak.
    /// This is the "brain" — could be intent router, LLM, or any handler.
    /// Return null or empty string to skip speaking and continue listening.
    /// </param>
    /// <param name="ct">Cancellation token to stop the loop.</param>
    public async Task StartConversationLoopAsync(
        Func<string, Task<string>> onHeard,
        CancellationToken ct = default)
    {
        await StartListeningLoopAsync(async text =>
        {
            try
            {
                var response = await onHeard(text);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var preview = response.Length > 80 ? response[..80] + "..." : response;
                    Console.WriteLine($"[VoiceLoop] Speaking: \"{preview}\"");
                    await SpeakAsync(response, ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceLoop] Response error: {ex.Message}");
            }
            return true; // Always continue listening
        }, ct);
    }

    /// <summary>
    /// Play raw WAV bytes through aplay.
    /// </summary>
    private static async Task PlayAudioAsync(byte[] wavData, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aplay",
            Arguments = "-q", // Quiet mode
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start aplay");

        await proc.StandardInput.BaseStream.WriteAsync(wavData, ct);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);
    }

    /// <summary>
    /// Synthesize text to WAV bytes using process wrapper fallback.
    /// </summary>
    private async Task<byte[]> SynthesizeToMemoryWithWrapperAsync(string text, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"locallizard-tts-{Path.GetRandomFileName()}.wav");
        try
        {
            await SynthesizeWithProcessWrapperAsync(text, tempPath, ct);
            return await File.ReadAllBytesAsync(tempPath, ct);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Basic process wrapper implementation for piper.
    /// </summary>
    private async Task<string> SynthesizeWithProcessWrapperAsync(string text, string outputPath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _config.PiperPath,
            Arguments = $"-m \"{_config.PiperModel}\" -f \"{outputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start piper");
        await proc.StandardInput.WriteAsync(text);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);

        return outputPath;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _whisperService?.Dispose();
            _whisperService = null;
            
            _piperService?.Dispose();
            _piperService = null;
            
            if (_brightnessGate.IsValueCreated)
                _brightnessGate.Value.Dispose();
            
            if (_alsaCapture.IsValueCreated)
                _alsaCapture.Value.Dispose();
            
            _disposed = true;
        }
    }
}