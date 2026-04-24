using System.Text;
using LocalLizard.Common;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Loads a GGUF model via LLamaSharp and provides chat-style text completion.
/// Note: Uses a simple chat template format rather than the model's native GGUF
/// template, because LLamaSharp's PromptTemplateTransformer fails with Gemma 4's
/// complex multimodal template (MissingTemplateException).
/// The simple format works because Gemma 4 understands:
///   <|turn>system\n...<turn|>\n<|turn>user\n...<turn|>\n<|turn>model\n
/// with BOS added automatically by the tokenizer (add_bos_token=true in GGUF).
/// </summary>
public sealed class LlmService : IDisposable
{
    private const string UserPrefix = "<|turn>user\n";
    private const string ModelPrefix = "<|turn>model\n";
    private const string TurnSep = "<turn|>\n";
    private const string SysPrefix = "<|turn>system\n";

    private readonly LizardConfig _config;
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private bool _disposed;

    public LlmService(LizardConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Load the model and create the primary context.
    /// Call once before any completion requests.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_weights is not null)
            return;

        await Task.Run(() =>
        {
            var parameters = new ModelParams(_config.ModelPath)
            {
                ContextSize = (uint)_config.LlmContextSize,
                GpuLayerCount = _config.LlmGpuLayers,
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _context = _weights.CreateContext(parameters);
        }, ct);
    }

    private DefaultSamplingPipeline CreateSamplingPipeline()
    {
        return new DefaultSamplingPipeline
        {
            Temperature = _config.LlmTemperature,
        };
    }

    /// <summary>
    /// Build a prompt in Gemma 4's chat format from history and user message.
    /// The tokenizer auto-prepends BOS token (add_bos_token=true).
    /// </summary>
    private static string BuildGemma4Prompt(
        string userMessage,
        string? systemPrompt,
        IReadOnlyList<(string Role, string Content)>? chatHistory)
    {
        var sb = new StringBuilder();

        // System prompt
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            sb.Append(SysPrefix);
            sb.Append(systemPrompt.Trim());
            sb.Append(TurnSep);
        }

        // Chat history (alternating user/assistant)
        if (chatHistory is not null)
        {
            foreach (var (role, content) in chatHistory)
            {
                bool isUser = role.Equals("user", StringComparison.OrdinalIgnoreCase);
                sb.Append(isUser ? UserPrefix : ModelPrefix);
                sb.Append(content.Trim());
                sb.Append(TurnSep);
            }
        }

        // Current user message
        sb.Append(UserPrefix);
        sb.Append(userMessage.Trim());
        sb.Append(TurnSep);

        // Generation prompt
        sb.Append(ModelPrefix);

        return sb.ToString();
    }

    /// <summary>
    /// Run a chat completion.
    /// </summary>
    public async Task<string> CompleteAsync(
        string userMessage,
        string? systemPrompt = null,
        IReadOnlyList<(string Role, string Content)>? chatHistory = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_weights is null || _context is null)
            throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");

        var prompt = BuildGemma4Prompt(userMessage, systemPrompt, chatHistory);

        var executor = new InteractiveExecutor(_context);
        var result = new StringBuilder();

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            SamplingPipeline = CreateSamplingPipeline(),
            AntiPrompts = ["<turn|>"],
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Simple non-chat completion for raw text prompts.
    /// </summary>
    public async Task<string> CompleteRawAsync(string prompt, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_weights is null || _context is null)
            throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");

        var executor = new InteractiveExecutor(_context);
        var result = new StringBuilder();

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            SamplingPipeline = CreateSamplingPipeline(),
            AntiPrompts = ["<turn|>"],
        };

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct))
        {
            result.Append(token);
        }

        return result.ToString().Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context?.Dispose();
        _weights?.Dispose();
    }
}
