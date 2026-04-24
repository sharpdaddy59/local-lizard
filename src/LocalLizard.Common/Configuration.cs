namespace LocalLizard.Common;

/// <summary>
/// Shared configuration for the LocalLizard pipeline.
/// Paths default to what's on brazos but can be overridden via environment variables.
/// </summary>
public sealed class LizardConfig
{
    public string ModelPath { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_MODEL_PATH")
        ?? "/home/wily/ai/models/gemma-4-E2B-it-Q4_K_M.gguf";

    public string WhisperPath { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_WHISPER_PATH")
        ?? "/home/wily/dev/whisper.cpp/main";

    public string PiperPath { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_PIPER_PATH")
        ?? "/home/wily/dev/piper/piper";

    public string PiperModel { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_PIPER_MODEL")
        ?? "/home/wily/dev/piper/en_US-hfc_female-medium.onnx";

    // Whisper.net configuration
    public string WhisperModelPath { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_WHISPER_MODEL_PATH")
        ?? "/home/wily/dev/whisper.cpp/models/ggml-base.en.bin";

    public string WhisperLanguage { get; set; } = "auto";
    public int WhisperThreads { get; set; } = 4;
    public bool WhisperUseGpu { get; set; } = false;

    public int LlmContextSize { get; set; } = 4096;
    public int LlmGpuLayers { get; set; } = 0; // CPU by default; bump for Vulkan
    public float LlmTemperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 512;

    // Wake word detection
    public string WakePhrase { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_WAKE_PHRASE")
        ?? "hey lizard";
    public double WakeWordCheckIntervalSec { get; set; } = 2.5;
    public double WakeWordCommandTimeoutSec { get; set; } = 10;
    public double WakeWordSilenceTimeoutSec { get; set; } = 1.5;

    // Telegram bot
    public string TelegramBotToken { get; set; } =
        Environment.GetEnvironmentVariable("LIZARD_TELEGRAM_BOT_TOKEN") ?? "";
}
