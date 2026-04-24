using System.Runtime.CompilerServices;
using LocalLizard.Common;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Wraps LLamaSharp model loading and text/vision completion.
/// Uses manual Gemma 4 chat format (avoiding PromptTemplateTransformer which
/// crashes with Gemma 4's complex multimodal Jinja template).
/// Creates a fresh context per completion call to avoid invalidInputBatch errors.
///
/// When an image buffer is provided, loads the mmproj, queues the image embedding,
/// and runs InteractiveExecutor in multimodal mode. Otherwise falls back to pure text.
/// </summary>
public sealed class LlmEngine : IDisposable
{
    private const string UserPrefix = "<|turn>user\n";
    private const string ModelPrefix = "<|turn>model\n";
    private const string TurnSep = "<turn|>\n";
    private const string SysPrefix = "<|turn>system\n";

    private LLamaWeights? _model;
    private MtmdWeights? _mtmd;
    private readonly LizardConfig _config;

    public LlmEngine(LizardConfig config)
    {
        _config = config;
    }

    public bool IsLoaded => _model is not null;

    /// <summary>
    /// True when both the text model and mmproj are loaded, meaning vision is available.
    /// </summary>
    public bool CanDoVision => _model is not null && _mtmd is not null;

    public void LoadModel()
    {
        var parameters = new ModelParams(_config.ModelPath)
        {
            ContextSize = (uint)_config.LlmContextSize,
            GpuLayerCount = _config.LlmGpuLayers,
        };

        _model = LLamaWeights.LoadFromFile(parameters);

        // Load mmproj if enabled and the file exists (non-fatal if missing)
        if (_config.VisionEnabled && File.Exists(_config.MmprojPath))
        {
            var mtmdParams = new MtmdContextParams
            {
                NThreads = Math.Max(1, _config.MtmdThreads),
                UseGpu = _config.MtmdGpuLayers > 0,
                Warmup = true,
            };

            _mtmd = MtmdWeights.LoadFromFile(_config.MmprojPath, _model, mtmdParams);
        }
    }

    private static readonly string SystemContext =
        $"{SysPrefix}You are a helpful voice assistant running on a mini PC. " +
        $"You can send text or voice replies. Keep responses conversational and concise.{TurnSep}";

    /// <summary>
    /// Build a prompt in Gemma 4's chat format.
    /// BOS is auto-prepended by the tokenizer (add_bos_token=true in GGUF).
    /// When providing an image, the caller must include the media marker (&lt;media&gt;)
    /// in the prompt text, or it will be appended automatically by the executor.
    /// </summary>
    private static string BuildPrompt(string userMessage)
    {
        return $"{SystemContext}{UserPrefix}{userMessage.Trim()}{TurnSep}{ModelPrefix}";
    }

    /// <summary>
    /// Text-only completion.
    /// </summary>
    public IAsyncEnumerable<string> CompleteAsync(string prompt,
        CancellationToken ct = default)
    {
        return CompleteAsync(prompt, imageBuffer: null, ct);
    }

    /// <summary>
    /// Completion with optional image input.
    /// Pass <paramref name="imageBuffer"/> = null for text-only (same as text-only overload).
    /// When imageBuffer is provided, the prompt should contain "&lt;media&gt;" where the
    /// image should be interpreted (typically at the start of the user message).
    /// If missing, the media marker is auto-appended by the executor.
    /// </summary>
    public async IAsyncEnumerable<string> CompleteAsync(string prompt,
        byte[]? imageBuffer,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);

        var parameters = new ModelParams(_config.ModelPath)
        {
            ContextSize = (uint)_config.LlmContextSize,
            GpuLayerCount = _config.LlmGpuLayers,
        };

        using var context = _model.CreateContext(parameters);
        var formattedPrompt = BuildPrompt(prompt);

        InteractiveExecutor executor;

        if (imageBuffer is not null && _mtmd is not null)
        {
            // Multimodal mode: create executor with mtmd weights, load the image
            executor = new InteractiveExecutor(context, _mtmd);
            executor.Embeds.Add(_mtmd.LoadMedia(imageBuffer.AsSpan()));
        }
        else
        {
            executor = new InteractiveExecutor(context);
        }

        // InteractiveExecutor does not implement IDisposable.
        // It references _context and _mtmd which are disposed by LlmEngine.
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
        _mtmd?.Dispose();
        _model?.Dispose();
    }
}
