using System.Text;
using LocalLizard.Common;
using LocalLizard.LocalLLM;
using LocalLizard.Voice;

namespace LocalLizard.Web.Services;

/// <summary>
/// Orchestrates the full conversation loop: STT → LLM → TTS.
/// Manages conversation history and provides both text-based and voice-based chat.
/// </summary>
public sealed class ChatLoopService : IDisposable
{
    private readonly LizardConfig _config;
    private readonly LlmEngine _llm;
    private readonly VoicePipeline _voice;
    private readonly List<(string Role, string Content)> _history = [];
    private readonly object _historyLock = new();
    private bool _disposed;

    public ChatLoopService(LizardConfig config, LlmEngine llm, VoicePipeline voice)
    {
        _config = config;
        _llm = llm;
        _voice = voice;
    }

    /// <summary>
    /// Full voice chat round-trip: audio bytes → transcribe → LLM → synthesize → audio bytes.
    /// </summary>
    public async Task<VoiceChatResponse> VoiceChatAsync(
        Stream audioStream,
        string? fileName = null,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Save audio to temp file and convert to WAV if needed
        var ext = string.IsNullOrEmpty(fileName) ? ".wav" : Path.GetExtension(fileName);
        var tempAudio = Path.Combine(Path.GetTempPath(), $"lizard-chat-in-{Guid.NewGuid():N}{ext}");
        await using (var fs = File.Create(tempAudio))
        {
            await audioStream.CopyToAsync(fs, ct);
        }

        try
        {
            var wavPath = tempAudio;
            if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                wavPath = await ConvertToWavAsync(tempAudio, ct);
            }

            // Step 2: STT
            var sttStart = sw.ElapsedMilliseconds;
            var userText = await _voice.TranscribeAsync(wavPath, ct);
            var sttMs = sw.ElapsedMilliseconds - sttStart;

            if (string.IsNullOrWhiteSpace(userText))
            {
                return new VoiceChatResponse
                {
                    Transcription = "",
                    Response = "",
                    Audio = [],
                    SttLatencyMs = sttMs,
                    LlmLatencyMs = 0,
                    TtsLatencyMs = 0,
                    TotalLatencyMs = sw.ElapsedMilliseconds,
                };
            }

            // Step 3: LLM
            var llmStart = sw.ElapsedMilliseconds;
            var replyText = await GetLlmResponseAsync(userText, ct);
            var llmMs = sw.ElapsedMilliseconds - llmStart;

            // Step 4: TTS
            var ttsStart = sw.ElapsedMilliseconds;
            var outputWav = Path.Combine(Path.GetTempPath(), $"lizard-chat-out-{Guid.NewGuid():N}.wav");
            await _voice.SynthesizeAsync(replyText, outputWav, ct);
            var audioBytes = await File.ReadAllBytesAsync(outputWav, ct);
            try { File.Delete(outputWav); } catch { }
            var ttsMs = sw.ElapsedMilliseconds - ttsStart;

            sw.Stop();

            return new VoiceChatResponse
            {
                Transcription = userText,
                Response = replyText,
                Audio = audioBytes,
                SttLatencyMs = sttMs,
                LlmLatencyMs = llmMs,
                TtsLatencyMs = ttsMs,
                TotalLatencyMs = sw.ElapsedMilliseconds,
            };
        }
        finally
        {
            try { File.Delete(tempAudio); } catch { }
        }
    }

    /// <summary>
    /// Text-only chat: message in → LLM → reply out.
    /// </summary>
    public async Task<TextChatResponse> TextChatAsync(string message, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var reply = await GetLlmResponseAsync(message, ct);

        return new TextChatResponse
        {
            UserMessage = message,
            Response = reply,
            LatencyMs = sw.ElapsedMilliseconds,
            HistoryLength = _history.Count,
        };
    }

    /// <summary>
    /// Get LLM response, maintaining conversation history.
    /// </summary>
    private async Task<string> GetLlmResponseAsync(string userMessage, CancellationToken ct)
    {
        if (!_llm.IsLoaded)
            _llm.LoadModel();

        var result = new StringBuilder();
        await foreach (var token in _llm.CompleteAsync(userMessage, ct))
        {
            result.Append(token);
        }

        var reply = result.ToString().Trim();

        lock (_historyLock)
        {
            _history.Add(("user", userMessage));
            _history.Add(("assistant", reply));
        }

        return reply;
    }

    /// <summary>
    /// Clear conversation history.
    /// </summary>
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    /// <summary>
    /// Get current conversation history length.
    /// </summary>
    public int HistoryCount
    {
        get { lock (_historyLock) { return _history.Count; } }
    }

    private static async Task<string> ConvertToWavAsync(string inputPath, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"lizard-converted-{Guid.NewGuid():N}.wav");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("ffmpeg not found");
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with exit code {proc.ExitCode}");

        return outputPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

public record VoiceChatResponse
{
    public string Transcription { get; init; } = "";
    public string Response { get; init; } = "";
    public byte[] Audio { get; init; } = [];
    public long SttLatencyMs { get; init; }
    public long LlmLatencyMs { get; init; }
    public long TtsLatencyMs { get; init; }
    public long TotalLatencyMs { get; init; }
}

public record TextChatResponse
{
    public string UserMessage { get; init; } = "";
    public string Response { get; init; } = "";
    public long LatencyMs { get; init; }
    public int HistoryLength { get; init; }
}
