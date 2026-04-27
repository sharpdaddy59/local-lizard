namespace LocalLizard.Common;

/// <summary>
/// Shared configuration for the LocalLizard pipeline.
/// Paths can be set via a <c>lizard-config.json</c> file next to the binary,
/// environment variables, or defaults.
///
/// Priority (highest first): JSON config file → environment variable → default value
/// 
/// JSON file format:
/// <code>
/// {
///   "BraveSearchApiKey": "...",
///   "TelegramBotToken": "..."
/// }
/// </code>
/// Only the fields you want to override need to be present.
/// </summary>
public sealed class LizardConfig
{
    private const string ConfigFileName = "lizard-config.json";

    // ---- Paths ----
    public string ModelPath { get; set; }
    public string MmprojPath { get; set; }
    public string WhisperPath { get; set; }
    public string PiperPath { get; set; }
    public string PiperModel { get; set; }
    public string WhisperModelPath { get; set; }
    public string MemoryFilePath { get; set; }
    public string ShellAllowlistPath { get; set; }

    // ---- Whisper ----
    public string WhisperLanguage { get; set; } = "auto";
    public int WhisperThreads { get; set; } = 4;
    public bool WhisperUseGpu { get; set; } = false;

    // ---- LLM ----
    public int LlmContextSize { get; set; } = 4096;
    public int LlmGpuLayers { get; set; } = 0;
    public float LlmTemperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 512;

    // ---- Vision ----
    public int MtmdGpuLayers { get; set; } = 0;
    public int MtmdThreads { get; set; } = 4;
    public bool VisionEnabled { get; set; } = true;

    // ---- Wake word ----
    public string WakePhrase { get; set; } = "hey lizard";
    public double WakeWordCheckIntervalSec { get; set; } = 2.5;
    public double WakeWordCommandTimeoutSec { get; set; } = 10;
    public double WakeWordSilenceTimeoutSec { get; set; } = 1.5;

    // ---- Audio capture ----
    public string AlsaDevice { get; set; } = "hw:1,0";
    public int CaptureMaxDurationMs { get; set; } = 5000;
    public int CaptureSilenceThresholdMs { get; set; } = 800;
    public double CaptureSilenceRms { get; set; } = 0.02;

    // ---- Camera brightness gate ----
    public string CameraDevice { get; set; } = "/dev/video0";
    public int CameraBrightnessThreshold { get; set; } = 10;
    public double CameraCheckIntervalSec { get; set; } = 5.0;
    public int CaptureMinAudioMs { get; set; } = 500;

    // ---- Secrets ----
    public string BraveSearchApiKey { get; set; } = "";
    public string TelegramBotToken { get; set; } = "";

    // ---- Features ----
    public bool ToolsEnabled { get; set; } = true;

    public LizardConfig()
    {
        // Load default values, then apply env vars, then apply JSON overrides
        SetDefaults();
        ApplyEnvironmentVariables();
        ApplyJsonConfig();
    }

    private void SetDefaults()
    {
        ModelPath = "/home/wily/ai/models/gemma-4-E2B-it-Q4_K_M.gguf";
        MmprojPath = "/home/wily/ai/models/mmproj-gemma4-E2B-BF16.gguf";
        WhisperPath = "/home/wily/dev/whisper.cpp/main";
        PiperPath = "/home/wily/dev/piper/piper";
        PiperModel = "/home/wily/dev/piper/en_US-hfc_female-medium.onnx";
        WhisperModelPath = "/home/wily/dev/whisper.cpp/models/ggml-base.en.bin";
        MemoryFilePath = "/shared/projects/local-lizard/memory.json";
        ShellAllowlistPath = "/shared/projects/local-lizard/config/shell-allowlist.json";
    }

    private void ApplyEnvironmentVariables()
    {
        SetFromEnv("LIZARD_MODEL_PATH", v => ModelPath = v);
        SetFromEnv("LIZARD_MMPROJ_PATH", v => MmprojPath = v);
        SetFromEnv("LIZARD_WHISPER_PATH", v => WhisperPath = v);
        SetFromEnv("LIZARD_PIPER_PATH", v => PiperPath = v);
        SetFromEnv("LIZARD_PIPER_MODEL", v => PiperModel = v);
        SetFromEnv("LIZARD_WHISPER_MODEL_PATH", v => WhisperModelPath = v);
        SetFromEnv("LIZARD_WHISPER_LANGUAGE", v => WhisperLanguage = v);
        SetFromEnv("LIZARD_WHISPER_THREADS", v => WhisperThreads = int.Parse(v));
        SetFromEnv("LIZARD_WHISPER_USE_GPU", v => WhisperUseGpu = bool.Parse(v));
        SetFromEnv("LIZARD_LLM_CONTEXT_SIZE", v => LlmContextSize = int.Parse(v));
        SetFromEnv("LIZARD_LLM_GPU_LAYERS", v => LlmGpuLayers = int.Parse(v));
        SetFromEnv("LIZARD_LLM_TEMPERATURE", v => LlmTemperature = float.Parse(v));
        SetFromEnv("LIZARD_MAX_TOKENS", v => MaxTokens = int.Parse(v));
        SetFromEnv("LIZARD_MTMD_GPU_LAYERS", v => MtmdGpuLayers = int.Parse(v));
        SetFromEnv("LIZARD_MTMD_THREADS", v => MtmdThreads = int.Parse(v));
        SetFromEnv("LIZARD_VISION_DISABLED", v => VisionEnabled = !bool.Parse(v));
        SetFromEnv("LIZARD_WAKE_PHRASE", v => WakePhrase = v);
        SetFromEnv("LIZARD_MEMORY_FILE", v => MemoryFilePath = v);
        SetFromEnv("LIZARD_SHELL_ALLOWLIST", v => ShellAllowlistPath = v);
        SetFromEnv("LIZARD_BRAVE_SEARCH_KEY", v => BraveSearchApiKey = v);
        SetFromEnv("LIZARD_TELEGRAM_BOT_TOKEN", v => TelegramBotToken = v);
        SetFromEnv("LIZARD_TOOLS_DISABLED", v => ToolsEnabled = !bool.Parse(v));
        SetFromEnv("LIZARD_ALSA_DEVICE", v => AlsaDevice = v);
        SetFromEnv("LIZARD_CAPTURE_MAX_DURATION_MS", v => CaptureMaxDurationMs = int.Parse(v));
        SetFromEnv("LIZARD_CAPTURE_SILENCE_MS", v => CaptureSilenceThresholdMs = int.Parse(v));
        SetFromEnv("LIZARD_CAPTURE_SILENCE_RMS", v => CaptureSilenceRms = double.Parse(v));
        SetFromEnv("LIZARD_CAMERA_DEVICE", v => CameraDevice = v);
        SetFromEnv("LIZARD_CAMERA_BRIGHTNESS_THRESHOLD", v => CameraBrightnessThreshold = int.Parse(v));
        SetFromEnv("LIZARD_CAMERA_CHECK_INTERVAL_SEC", v => CameraCheckIntervalSec = double.Parse(v));
        SetFromEnv("LIZARD_CAPTURE_MIN_AUDIO_MS", v => CaptureMinAudioMs = int.Parse(v));
    }

    private void ApplyJsonConfig()
    {
        // Load from the project-level config directory.
        // This is the single source of truth for persistent configuration.
        // Environment variables still take precedence (applied before this).
        var projectConfig = "/shared/projects/local-lizard/config/lizard-config.json";
        var configPath = File.Exists(projectConfig) ? projectConfig : null;

        if (configPath is null)
            return;

        try
        {
            var json = File.ReadAllText(configPath);
            var overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            if (overrides is null)
                return;

            foreach (var kvp in overrides)
            {
                switch (kvp.Key)
                {
                    case "ModelPath": ModelPath = kvp.Value.GetString() ?? ModelPath; break;
                    case "MmprojPath": MmprojPath = kvp.Value.GetString() ?? MmprojPath; break;
                    case "WhisperPath": WhisperPath = kvp.Value.GetString() ?? WhisperPath; break;
                    case "PiperPath": PiperPath = kvp.Value.GetString() ?? PiperPath; break;
                    case "PiperModel": PiperModel = kvp.Value.GetString() ?? PiperModel; break;
                    case "WhisperModelPath": WhisperModelPath = kvp.Value.GetString() ?? WhisperModelPath; break;
                    case "WhisperLanguage": WhisperLanguage = kvp.Value.GetString() ?? WhisperLanguage; break;
                    case "WhisperThreads": WhisperThreads = kvp.Value.GetInt32(); break;
                    case "WhisperUseGpu": WhisperUseGpu = kvp.Value.GetBoolean(); break;
                    case "LlmContextSize": LlmContextSize = kvp.Value.GetInt32(); break;
                    case "LlmGpuLayers": LlmGpuLayers = kvp.Value.GetInt32(); break;
                    case "LlmTemperature": LlmTemperature = kvp.Value.GetSingle(); break;
                    case "MaxTokens": MaxTokens = kvp.Value.GetInt32(); break;
                    case "MtmdGpuLayers": MtmdGpuLayers = kvp.Value.GetInt32(); break;
                    case "MtmdThreads": MtmdThreads = kvp.Value.GetInt32(); break;
                    case "VisionEnabled": VisionEnabled = kvp.Value.GetBoolean(); break;
                    case "WakePhrase": WakePhrase = kvp.Value.GetString() ?? WakePhrase; break;
                    case "MemoryFilePath": MemoryFilePath = kvp.Value.GetString() ?? MemoryFilePath; break;
                    case "ShellAllowlistPath": ShellAllowlistPath = kvp.Value.GetString() ?? ShellAllowlistPath; break;
                    case "BraveSearchApiKey": BraveSearchApiKey = kvp.Value.GetString() ?? BraveSearchApiKey; break;
                    case "TelegramBotToken": TelegramBotToken = kvp.Value.GetString() ?? TelegramBotToken; break;
                    case "ToolsEnabled": ToolsEnabled = kvp.Value.GetBoolean(); break;
                    case "AlsaDevice": AlsaDevice = kvp.Value.GetString() ?? AlsaDevice; break;
                    case "CaptureMaxDurationMs": CaptureMaxDurationMs = kvp.Value.GetInt32(); break;
                    case "CaptureSilenceThresholdMs": CaptureSilenceThresholdMs = kvp.Value.GetInt32(); break;
                    case "CaptureSilenceRms": CaptureSilenceRms = kvp.Value.GetDouble(); break;
                    case "CameraDevice": CameraDevice = kvp.Value.GetString() ?? CameraDevice; break;
                    case "CameraBrightnessThreshold": CameraBrightnessThreshold = kvp.Value.GetInt32(); break;
                    case "CameraCheckIntervalSec": CameraCheckIntervalSec = kvp.Value.GetDouble(); break;
                    case "CaptureMinAudioMs": CaptureMinAudioMs = kvp.Value.GetInt32(); break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LizardConfig] Failed to load {configPath}: {ex.Message}");
        }
    }

    private void SetFromEnv(string variable, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value))
            setter(value);
    }
}
