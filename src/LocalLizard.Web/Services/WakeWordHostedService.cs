using LocalLizard.Common;
using LocalLizard.Voice;

namespace LocalLizard.Web.Services;

/// <summary>
/// Hosted service that runs wake word detection in the background.
/// When a wake word + command is detected, automatically processes it
/// through the chat loop and speaks the response.
/// </summary>
public sealed class WakeWordHostedService : IHostedService, IDisposable
{
    private readonly WakeWordService _wakeWordService;
    private readonly ChatLoopService _chatLoop;
    private readonly VoicePipeline _voice;
    private readonly LizardConfig _config;
    private bool _disposed;

    public bool IsListening => _wakeWordService.IsListening;
    public string WakePhrase => _wakeWordService.WakePhrase;

    public WakeWordHostedService(
        LizardConfig config,
        ChatLoopService chatLoop,
        VoicePipeline voice)
    {
        _config = config;
        _chatLoop = chatLoop;
        _voice = voice;

        // Create Whisper factory for wake word detection
        var factory = Whisper.net.WhisperFactory.FromPath(config.WhisperModelPath);
        _wakeWordService = new WakeWordService(config, factory);

        // Wire up events
        _wakeWordService.CommandRecorded += OnCommandRecorded;
        _wakeWordService.ListenError += OnListenError;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Don't auto-start; wait for explicit /api/wakeword/start call
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        return _wakeWordService.StopListeningAsync();
    }

    public void StartListening()
    {
        _wakeWordService.StartListening();
    }

    public Task StopListeningAsync()
    {
        return _wakeWordService.StopListeningAsync();
    }

    private async void OnCommandRecorded(object? sender, CommandRecordedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(e.Transcription))
            {
                Console.WriteLine("[WakeWordHosted] Empty command, ignoring");
                return;
            }

            Console.WriteLine($"[WakeWordHosted] Processing command: \"{e.Transcription}\"");

            // Process through chat loop (text-only, faster than full voice round-trip)
            var result = await _chatLoop.TextChatAsync(e.Transcription);

            Console.WriteLine($"[WakeWordHosted] Response ({result.LatencyMs}ms): \"{result.Response}\"");

            // Synthesize response to audio (could play through speakers in future)
            var outputPath = Path.Combine(Path.GetTempPath(), $"lizard-wake-response-{Guid.NewGuid():N}.wav");
            await _voice.SynthesizeAsync(result.Response, outputPath);
            
            Console.WriteLine($"[WakeWordHosted] Response audio saved to {outputPath}");

            // TODO: Play audio through speakers when speaker output is wired up
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WakeWordHosted] Error processing command: {ex.Message}");
        }
    }

    private void OnListenError(object? sender, LocalLizard.Voice.ErrorEventArgs e)
    {
        Console.WriteLine($"[WakeWordHosted] Listen error: {e.Exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wakeWordService.Dispose();
    }
}
