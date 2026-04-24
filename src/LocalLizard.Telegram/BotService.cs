using System.Diagnostics;
using System.Text;
using LocalLizard.Common;
using LocalLizard.LocalLLM;
using LocalLizard.Voice;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace LocalLizard.Telegram;

/// <summary>
/// Telegram bot service that bridges Telegram messages to the LocalLizard pipeline.
/// Supports text messages, voice notes (STT → LLM → TTS → voice reply),
/// and text replies for text input.
/// </summary>
public sealed class BotService : IDisposable
{
    private readonly ITelegramBotClient _bot;
    private readonly LlmEngine _llm;
    private readonly VoicePipeline _voice;
    private readonly LizardConfig _config;
    private readonly HashSet<long> _allowedChatIds;
    private readonly CancellationTokenSource _cts = new();

    // Per-chat conversation state
    private readonly Dictionary<long, List<(string Role, string Content)>> _conversations = new();
    private readonly object _convLock = new();

    public BotService(ITelegramBotClient bot, LlmEngine llm, VoicePipeline voice, LizardConfig config)
    {
        _bot = bot;
        _llm = llm;
        _voice = voice;
        _config = config;

        // Allowed chat IDs from env (comma-separated). If empty, allow all.
        var allowedEnv = Environment.GetEnvironmentVariable("LIZARD_TELEGRAM_CHAT_IDS");
        _allowedChatIds = string.IsNullOrEmpty(allowedEnv)
            ? new HashSet<long>()
            : allowedEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(long.Parse).ToHashSet();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var me = await _bot.GetMe(ct);
        Console.WriteLine($"[TelegramBot] Connected as @{me.Username}");

        _bot.StartReceiving(
            async (botClient, update, token) => await HandleUpdateAsync(update, token),
            async (botClient, exception, token) =>
            {
                Console.WriteLine($"[TelegramBot] Error: {exception.Message}");
                await Task.CompletedTask;
            },
            cancellationToken: _cts.Token
        );

        // Keep running until cancelled
        try { await Task.Delay(Timeout.Infinite, _cts.Token); }
        catch (OperationCanceledException) { }
    }

    public void Stop() => _cts.Cancel();

    private async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Type != UpdateType.Message || update.Message is null) return;

        var msg = update.Message;
        var chatId = msg.Chat.Id;

        // Auth check
        if (_allowedChatIds.Count > 0 && !_allowedChatIds.Contains(chatId))
        {
            await _bot.SendMessage(chatId, "⛔ Unauthorized.", cancellationToken: ct);
            return;
        }

        try
        {
            if (msg.Type == MessageType.Voice || msg.Type == MessageType.Audio)
            {
                await HandleVoiceMessageAsync(msg, ct);
            }
            else if (msg.Type == MessageType.Text)
            {
                await HandleTextMessageAsync(msg, ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TelegramBot] Error handling message: {ex}");
            await _bot.SendMessage(chatId, $"⚠️ Error: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task HandleTextMessageAsync(Message msg, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var userText = msg.Text!;

        // Commands
        if (userText.StartsWith('/'))
        {
            await HandleCommandAsync(msg, userText, ct);
            return;
        }

        // Send "typing" indicator
        await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var reply = await GetLlmResponseAsync(chatId, userText, ct);
        await _bot.SendMessage(chatId, reply, cancellationToken: ct);
    }

    private async Task HandleVoiceMessageAsync(Message msg, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var fileId = msg.Voice?.FileId ?? msg.Audio!.FileId;

        await _bot.SendChatAction(chatId, ChatAction.RecordVoice, cancellationToken: ct);

        // Download the voice file
        var file = await _bot.GetFile(fileId, ct);
        var oggPath = Path.Combine(Path.GetTempPath(), $"lizard-tg-{Guid.NewGuid():N}.ogg");

        await using (var fs = File.Create(oggPath))
        {
            await _bot.DownloadFile(file.FilePath!, fs, cancellationToken: ct);
        }

        try
        {
            // Convert OGG to WAV
            var wavPath = await ConvertToWavAsync(oggPath, ct);

            // STT
            var transcription = await _voice.TranscribeAsync(wavPath, ct);

            if (string.IsNullOrWhiteSpace(transcription))
            {
                await _bot.SendMessage(chatId, "🔇 Couldn't understand that. Try again?", cancellationToken: ct);
                return;
            }

            // LLM
            var reply = await GetLlmResponseAsync(chatId, transcription, ct);

            // TTS
            var replyWav = Path.Combine(Path.GetTempPath(), $"lizard-tg-reply-{Guid.NewGuid():N}.wav");
            await _voice.SynthesizeAsync(reply, replyWav, ct);

            // Convert WAV to OGG/Opus for Telegram voice note
            var replyOgg = await ConvertWavToOggAsync(replyWav, ct);

            // Send voice reply + text transcription
            await using var voiceStream = File.OpenRead(replyOgg);
            await _bot.SendVoice(chatId, new InputFileStream(voiceStream), caption: $"🎤 \"{transcription}\"\n\n{reply}", cancellationToken: ct);

            try { File.Delete(replyWav); } catch { }
            try { File.Delete(replyOgg); } catch { }
        }
        finally
        {
            try { File.Delete(oggPath); } catch { }
        }
    }

    private async Task HandleCommandAsync(Message msg, string command, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var cmd = command.Split('@')[0].ToLowerInvariant(); // Strip bot username suffix

        switch (cmd)
        {
            case "/start":
                await _bot.SendMessage(chatId,
                    "🦎 **LocalLizard Bot**\n\n" +
                    "Send me a text message or voice note and I'll respond!\n\n" +
                    "Commands:\n" +
                    "/start — Show this message\n" +
                    "/clear — Clear conversation history\n" +
                    "/history — Show history length\n" +
                    "/status — Bot status",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                break;

            case "/clear":
                ClearHistory(chatId);
                await _bot.SendMessage(chatId, "🗑️ Conversation history cleared.", cancellationToken: ct);
                break;

            case "/history":
                var count = GetHistoryLength(chatId);
                await _bot.SendMessage(chatId, $"📝 Conversation has {count} messages.", cancellationToken: ct);
                break;

            case "/status":
                var modelLoaded = _llm.IsLoaded;
                await _bot.SendMessage(chatId,
                    $"🦎 **LocalLizard Status**\n" +
                    $"Model loaded: {(modelLoaded ? "✅" : "❌")}\n" +
                    $"Wake phrase: \"{_config.WakePhrase}\"",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                break;

            default:
                break; // Ignore unknown commands
        }
    }

    private async Task<string> GetLlmResponseAsync(long chatId, string userMessage, CancellationToken ct)
    {
        if (!_llm.IsLoaded)
            _llm.LoadModel();

        var result = new StringBuilder();
        await foreach (var token in _llm.CompleteAsync(userMessage, ct))
        {
            result.Append(token);
        }

        var reply = result.ToString().Trim();

        lock (_convLock)
        {
            if (!_conversations.TryGetValue(chatId, out var history))
            {
                history = [];
                _conversations[chatId] = history;
            }
            history.Add(("user", userMessage));
            history.Add(("assistant", reply));
        }

        return reply;
    }

    private void ClearHistory(long chatId)
    {
        lock (_convLock)
        {
            _conversations.Remove(chatId);
        }
    }

    private int GetHistoryLength(long chatId)
    {
        lock (_convLock)
        {
            return _conversations.TryGetValue(chatId, out var h) ? h.Count : 0;
        }
    }

    private static async Task<string> ConvertToWavAsync(string inputPath, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"lizard-converted-{Guid.NewGuid():N}.wav");
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg not found");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with exit code {proc.ExitCode}");
        return outputPath;
    }

    private static async Task<string> ConvertWavToOggAsync(string wavPath, CancellationToken ct)
    {
        var oggPath = Path.Combine(Path.GetTempPath(), $"lizard-reply-{Guid.NewGuid():N}.ogg");
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{wavPath}\" -c:a libopus -b:a 64k \"{oggPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg not found");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg to ogg failed with exit code {proc.ExitCode}");
        return oggPath;
    }

    public void Dispose() => _cts.Cancel();
}
