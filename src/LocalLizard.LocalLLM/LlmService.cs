using System.Text;
using LocalLizard.Common;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Loads a GGUF model via LLamaSharp and provides chat-style text completion.
/// </summary>
public sealed class LlmService : IDisposable
{
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
    /// Run a chat completion using the session API with history management.
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

        var executor = new InteractiveExecutor(_context);

        var history = new ChatHistory();
        history.AddMessage(AuthorRole.System,
            systemPrompt ?? "You are a helpful, concise assistant running locally on a small device. Keep responses short.");

        if (chatHistory is not null)
        {
            foreach (var (role, content) in chatHistory)
            {
                var authorRole = role.Equals("user", StringComparison.OrdinalIgnoreCase)
                    ? AuthorRole.User
                    : AuthorRole.Assistant;
                history.AddMessage(authorRole, content);
            }
        }

        var session = new ChatSession(executor, history);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            SamplingPipeline = CreateSamplingPipeline(),
            AntiPrompts = ["<end_of_turn>", "\nUser:", "\nuser:"],
        };

        var result = new StringBuilder();
        await foreach (var token in session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, userMessage),
            inferenceParams,
            ct))
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
