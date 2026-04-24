using LocalLizard.Common;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Wraps LLamaSharp model loading and basic text completion.
/// </summary>
public sealed class LlmEngine : IDisposable
{
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private ChatSession? _session;
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
        _context = _model.CreateContext(parameters);
        var executor = new InteractiveExecutor(_context);
        var history = new ChatHistory();
        history.AddMessage(AuthorRole.System, "You are a helpful local assistant running on a mini PC.");
        _session = new ChatSession(executor, history);
    }

    public async IAsyncEnumerable<string> CompleteAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_session is null, this);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            AntiPrompts = ["User:"],
            SamplingPipeline = new DefaultSamplingPipeline(),
        };

        await foreach (var text in _session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, prompt),
            inferenceParams))
        {
            yield return text;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
        _model?.Dispose();
    }
}
