using System.Runtime.CompilerServices;
using LocalLizard.Common;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Wraps LLamaSharp model loading and basic text completion.
/// Uses manual Gemma 4 chat format (avoiding PromptTemplateTransformer which
/// crashes with Gemma 4's complex multimodal Jinja template).
/// Creates a fresh context per completion call to avoid invalidInputBatch errors.
/// </summary>
public sealed class LlmEngine : IDisposable
{
    private const string UserPrefix = "<|turn>user\n";
    private const string ModelPrefix = "<|turn>model\n";
    private const string TurnSep = "<turn|>\n";
    private const string SysPrefix = "<|turn>system\n";

    private LLamaWeights? _model;
    private readonly LizardConfig _config;

    public LlmEngine(LizardConfig config)
    {
        _config = config;
    }

    public bool IsLoaded => _model is not null;

    public void LoadModel()
    {
        var parameters = new ModelParams(_config.ModelPath)
        {
            ContextSize = (uint)_config.LlmContextSize,
            GpuLayerCount = _config.LlmGpuLayers,
        };

        _model = LLamaWeights.LoadFromFile(parameters);
    }

    private static readonly string SystemContext =
        $"{SysPrefix}You are a helpful voice assistant running on a mini PC. " +
        $"You can send text or voice replies. Keep responses conversational and concise.{TurnSep}";

    /// <summary>
    /// Build a prompt in Gemma 4's chat format.
    /// BOS is auto-prepended by the tokenizer (add_bos_token=true in GGUF).
    /// </summary>
    private static string BuildPrompt(string userMessage)
    {
        return $"{SystemContext}{UserPrefix}{userMessage.Trim()}{TurnSep}{ModelPrefix}";
    }

    public async IAsyncEnumerable<string> CompleteAsync(string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);

        // Create a fresh context per call to avoid invalidInputBatch errors
        // from stale KV cache state. Model weights are shared via _model.
        var parameters = new ModelParams(_config.ModelPath)
        {
            ContextSize = (uint)_config.LlmContextSize,
            GpuLayerCount = _config.LlmGpuLayers,
        };

        using var context = _model.CreateContext(parameters);
        var formattedPrompt = BuildPrompt(prompt);
        var executor = new InteractiveExecutor(context);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            AntiPrompts = ["<turn|>"],
            SamplingPipeline = new DefaultSamplingPipeline(),
        };

        await foreach (var text in executor.InferAsync(formattedPrompt, inferenceParams, ct))
        {
            yield return text;
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
    }
}
