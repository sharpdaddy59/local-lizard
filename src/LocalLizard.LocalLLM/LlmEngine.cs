using System.Runtime.CompilerServices;
using LocalLizard.Common;
using LocalLizard.LocalLLM.Tools;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace LocalLizard.LocalLLM;

/// <summary>
/// Wraps LLamaSharp model loading and text/vision completion.
/// Uses manual Gemma 4 chat format (avoiding PromptTemplateTransformer which
/// crashes with Gemma 4's complex multimodal Jinja template).
/// Creates context once at startup and reuses it across calls for performance.
/// The InteractiveExecutor is created fresh per inference to avoid state bleed
/// between turns (the executor handles its own state internally).
///
/// When an image buffer is provided, loads the mmproj, queues the image embedding,
/// and runs InteractiveExecutor in multimodal mode. Otherwise falls back to pure text.
///
/// Tool calling: CompleteWithToolsAsync runs a loop of generate → detect tools →
/// execute → inject results → regenerate. The tool loop is transparent — only the
/// final non-tool output is yielded.
/// </summary>
public sealed class LlmEngine : IDisposable
{
    private const string UserPrefix = "<|turn>user\n";
    private const string ModelPrefix = "<|turn>model\n";
    private const string TurnSep = "<turn|>\n";
    private const string SysPrefix = "<|turn>system\n";

    private LLamaWeights? _model;
    private MtmdWeights? _mtmd;
    private LLamaContext? _context;
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

        // Create a persistent context for all inference calls.
        // Previously this was done per-call to work around invalidInputBatch
        // errors, but modern LLamaSharp handles context reuse correctly when
        // the InteractiveExecutor is recreated per inference (the executor
        // manages its own KV cache state internally).
        _context = _model.CreateContext(parameters);

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
    private string BuildPrompt(string userMessage)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_combinedSystemContext);
        sb.Append(UserPrefix);
        sb.Append(userMessage.Trim());
        sb.Append(TurnSep);
        sb.Append(ModelPrefix);
        return sb.ToString();
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
        ObjectDisposedException.ThrowIf(_context is null, this);

        var formattedPrompt = BuildPrompt(prompt);

        InteractiveExecutor executor;

        if (imageBuffer is not null && _mtmd is not null)
        {
            // Multimodal mode: create executor with mtmd weights, load the image
            executor = new InteractiveExecutor(_context, _mtmd);
            executor.Embeds.Add(_mtmd.LoadMedia(imageBuffer.AsSpan()));
        }
        else
        {
            executor = new InteractiveExecutor(_context);
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
        _context?.Dispose();
        _mtmd?.Dispose();
        _model?.Dispose();
    }

    /// <summary>
    /// Combined system context (base + optional tools), pre-built on tool prompt change.
    /// </summary>
    private string _combinedSystemContext = SystemContext;

    /// <summary>
    /// Optional tool system prompt injected after the base system context.
    /// </summary>
    private string? _toolSystemPrompt;

    /// <summary>
    /// Set an additional system prompt describing available tools.
    /// Rebuilds the combined system context into a single &lt;|turn&gt;system block.
    /// </summary>
    public void SetToolSystemPrompt(string prompt)
    {
        _toolSystemPrompt = prompt;
        if (string.IsNullOrEmpty(prompt))
        {
            _combinedSystemContext = SystemContext;
        }
        else
        {
            // Merge base system context and tool declarations into one &lt;|turn&gt;system block.
            // Gemma 4 expects a single system turn, not consecutive system blocks.
            var baseText = SystemContext.TrimEnd();
            // Remove trailing <turn|> from baseText
            if (baseText.EndsWith(TurnSep.TrimEnd()))
                baseText = baseText[..^TurnSep.TrimEnd().Length];
            _combinedSystemContext = $"{SysPrefix}{baseText}\n\n{prompt.Trim()}{TurnSep}";
        }
    }

    // ---- Tool calling ----

    /// <summary>
    /// Current tool execution pipeline, set via ConfigureTools().
    /// When null, tools are disabled (pure chat mode).
    /// </summary>
    private ToolExecutionPipeline? _toolPipeline;

    /// <summary>
    /// Configure tool support. Pass null to disable tools.
    /// </summary>
    public void ConfigureTools(ToolExecutionPipeline? pipeline)
    {
        _toolPipeline = pipeline;
    }

    /// <summary>
    /// Whether tools are currently configured and ready.
    /// </summary>
    public bool ToolsConfigured => _toolPipeline is not null;

    /// <summary>
    /// Maximum tool call iterations before giving up (safety limit).
    /// </summary>
    public int MaxToolIterations { get; set; } = 5;

    /// <summary>
    /// Completion with tool call support.
    /// Runs the generate → detect tools → execute → inject results → regenerate
    /// loop transparently. Only the final clean output is yielded.
    ///
    /// Maintains the full conversation across iterations to preserve context.
    /// Tool results are injected as a user turn (<|turn>user\n[TOOL_RESULT]...<turn|>\n<|turn>model\n)
    /// which is faithful to Gemma 4's turn-alternation training format.
    /// </summary>
    public IAsyncEnumerable<string> CompleteWithToolsAsync(
        string prompt,
        byte[]? imageBuffer = null,
        CancellationToken ct = default)
    {
        return CompleteWithToolsAsyncImpl(prompt, imageBuffer, ct);
    }

    private async IAsyncEnumerable<string> CompleteWithToolsAsyncImpl(
        string userMessage,
        byte[]? imageBuffer,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_toolPipeline is null)
        {
            // No tools: pass through to normal completion
            await foreach (var token in CompleteAsync(userMessage, imageBuffer, ct))
                yield return token;
            yield break;
        }

        // Build the full conversation accumulator.
        // Format: combined system context + turns that accumulate across iterations.
        var conversation = new System.Text.StringBuilder();
        conversation.Append(_combinedSystemContext);

        // First user turn
        conversation.Append(UserPrefix);
        conversation.Append(userMessage.Trim());
        conversation.Append(TurnSep);
        conversation.Append(ModelPrefix);

        // Track whether any iteration produced clean (non-tool-call) output
        bool anyCleanOutput = false;

        for (int iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            // Generate using full accumulated conversation as raw prompt
            var outputBuilder = new System.Text.StringBuilder();
            await foreach (var token in InferRawAsync(conversation.ToString(), imageBuffer, ct))
            {
                outputBuilder.Append(token);
            }

            var output = outputBuilder.ToString();

            // Check for tool calls
            var toolResult = await _toolPipeline.ProcessOutputAsync(output, ct);
            if (toolResult is null)
            {
                // No tool calls — this is the final response. Yield the clean text.
                anyCleanOutput = true;
                yield return output;
                yield break;
            }

            // Tool calls found.
            // Append the model's output (with tool calls stripped) to the conversation
            var cleanOutput = toolResult.CleanOutput;
            if (!string.IsNullOrEmpty(cleanOutput))
            {
                anyCleanOutput = true;
                // The model generated some text before the tool call — keep it
                conversation.Append(cleanOutput);
            }
            conversation.Append(TurnSep);

            // Inject tool results using Gemma 4's native <|tool_response> format.
            // Unlike the old [TOOL_RESULT] user-turn injection, this continues the
            // model turn. The model stopped at EOG token 50 (<|tool_response>), so
            // we inject the response as if the model itself generated it.
            foreach (var r in toolResult.Results)
            {
                conversation.Append(ToolCallParser.FormatResult(r.Name, r.Status, r.Output, r.Truncated));
            }
            conversation.Append(TurnSep);

            // Prepare for the model to continue or respond to the tool results
            conversation.Append(ModelPrefix);

            // Image only valid for first generation
            imageBuffer = null;
        }

        // Dead letter: max iterations reached and all iterations produced tool calls.
        // Fall back to plain completion (no tool system prompt, no pipeline).
        // This prevents the model from looping endlessly on tool calls.
        if (!anyCleanOutput)
        {
            // Temporarily remove tool system prompt so the model responds naturally
            var savedPrompt = _toolSystemPrompt;
            _toolSystemPrompt = null;
            await foreach (var token in CompleteAsync(userMessage, imageBuffer, ct))
                yield return token;
            _toolSystemPrompt = savedPrompt;
        }
        else
        {
            // We had some clean output but hit the iteration limit. Try one more time.
            await foreach (var token in InferRawAsync(conversation.ToString(), imageBuffer, ct))
                yield return token;
        }
    }

    /// <summary>
    /// Raw inference using the exact prompt text without BuildPrompt wrapping.
    /// Used by the tool loop to inject full conversation turns directly.
    /// </summary>
    private async IAsyncEnumerable<string> InferRawAsync(
        string rawPrompt,
        byte[]? imageBuffer,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);
        ObjectDisposedException.ThrowIf(_context is null, this);

        InteractiveExecutor executor;
        if (imageBuffer is not null && _mtmd is not null)
        {
            executor = new InteractiveExecutor(_context, _mtmd);
            executor.Embeds.Add(_mtmd.LoadMedia(imageBuffer.AsSpan()));
        }
        else
        {
            executor = new InteractiveExecutor(_context);
        }

        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            AntiPrompts = ["<turn|>"],
            SamplingPipeline = new DefaultSamplingPipeline(),
        };

        await foreach (var token in executor.InferAsync(rawPrompt, inferenceParams, ct))
        {
            yield return token;
        }
    }
}
