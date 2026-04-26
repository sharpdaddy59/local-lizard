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
/// Uses LLamaTemplate (GGUF's native chat template) for model-agnostic prompt
/// formatting — compatible with Qwen, Gemma, Llama, etc. without per-model
/// configuration.
///
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
    // Deterministic intent router
    private readonly IntentRouter _intentRouter = new();

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

    /// <summary>
    /// Build a chat prompt using the GGUF's native chat template.
    /// BOS/EOS tokens are handled automatically by the template engine.
    /// </summary>
    private string BuildPrompt(List<ChatMessage> messages)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);

        var template = new LLamaTemplate(_model, strict: true) { AddAssistant = true };
        foreach (var msg in messages)
        {
            template.Add(msg.Role, msg.Content);
        }
        return LLamaTemplate.Encoding.GetString(template.Apply().ToArray());
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

        // Clear KV cache for the default sequence before each inference.
        _context.NativeHandle.MemorySequenceRemove(LLamaSeqId.Zero, (LLamaPos)0, (LLamaPos)int.MaxValue);

        // Build conversation from combined system context + user message
        var messages = new List<ChatMessage>();

        // System message: combine base system with tool definitions
        var systemContent = GetSystemContent();
        if (!string.IsNullOrEmpty(systemContent))
        {
            messages.Add(new ChatMessage(ChatMessage.System, systemContent));
        }

        messages.Add(new ChatMessage(ChatMessage.User, prompt));

        var formattedPrompt = BuildPrompt(messages);

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

        // LLamaTemplate applies the correct EOT token from the GGUF, so we
        // use a broad anti-prompt set as a safety net.
        var inferenceParams = new InferenceParams
        {
            MaxTokens = _config.MaxTokens,
            AntiPrompts = CommonAntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _config.LlmTemperature,
                FrequencyPenalty = 0.0f,
                PresencePenalty = 0.0f,
                RepeatPenalty = 1.1f,
            },
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

    // ---- System prompt & tool management ----

    /// <summary>
    /// Anti-prompts as a last line of defense. When the model's chat template
    /// handles EOT correctly the native tokens already stop generation, but
    /// these catch cases where the template metadata is missing or the model
    /// leaks a literal marker.
    /// </summary>
    private static readonly string[] CommonAntiPrompts =
    {
        "<|im_end|>",       // Qwen / ChatML
        "<end_of_turn>",    // Gemma
        "<|eot_id|>",       // Llama 3.x
        "<|endoftext|>",    // generic
    };

    /// <summary>
    /// The base system prompt.
    /// </summary>
    private string _baseSystemPrompt =
        "You are a helpful voice assistant running on a mini PC. " +
        "You can send text or voice replies. Keep responses conversational and concise. " +
        "Do not use thinking or reasoning tags. Be direct.";

    /// <summary>
    /// Base system content without tool definitions.
    /// </summary>
    public string BaseSystemPrompt
    {
        get => _baseSystemPrompt;
        set => _baseSystemPrompt = value;
    }

    /// <summary>
    /// Optional tool declarations injected after the base system prompt.
    /// </summary>
    private string? _toolDeclarations;

    /// <summary>
    /// Get the combined system content (base + tool declarations).
    /// </summary>
    private string GetSystemContent()
    {
        if (string.IsNullOrEmpty(_toolDeclarations))
            return _baseSystemPrompt;
        return _baseSystemPrompt + "\n\n" + _toolDeclarations;
    }

    /// <summary>
    /// Set tool declarations that will be appended to the base system prompt.
    /// Pass null or empty to clear.
    /// </summary>
    public void SetToolSystemPrompt(string declarations)
    {
        _toolDeclarations = string.IsNullOrWhiteSpace(declarations) ? null : declarations;
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
    /// Tool results are injected using the model's native tool response format
    /// (injected as additional ChatMessage.Tool messages in the template).
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

        // Attempt deterministic intent routing first (no LLM inference)
        var routerResult = await _intentRouter.TryRouteAsync(userMessage, _toolPipeline.Tools, ct);
        if (routerResult is not null)
        {
            yield return routerResult;
            yield break;
        }

        // Build the conversation as typed messages (not raw text).
        // LLamaTemplate renders them using the GGUF's native chat template.
        var messages = new List<ChatMessage>();

        // System message: combine base system with tool definitions
        var systemContent = GetSystemContent();
        if (!string.IsNullOrEmpty(systemContent))
        {
            messages.Add(new ChatMessage(ChatMessage.System, systemContent));
        }

        // First user turn
        messages.Add(new ChatMessage(ChatMessage.User, userMessage));

        // Track whether any iteration produced clean (non-tool-call) output
        bool anyCleanOutput = false;

        for (int iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            // Build prompt from messages and run inference
            var formattedPrompt = BuildPrompt(messages);

            var outputBuilder = new System.Text.StringBuilder();
            await foreach (var token in InferRawAsync(
                formattedPrompt, imageBuffer, ct))
            {
                outputBuilder.Append(token);
            }

            var output = outputBuilder.ToString();

            // Check for tool calls
            var toolResult = await _toolPipeline!.ProcessOutputAsync(output, ct);
            if (toolResult is null)
            {
                // No tool calls — this is the final response. Yield the clean text.
                anyCleanOutput = true;
                yield return output;
                yield break;
            }

            // Tool calls found.
            // Add the model's output (with tool calls stripped) as an assistant message
            var cleanOutput = toolResult.CleanOutput;
            if (!string.IsNullOrEmpty(cleanOutput))
            {
                anyCleanOutput = true;
                messages.Add(new ChatMessage(ChatMessage.Assistant, cleanOutput));
            }
            else if (!string.IsNullOrEmpty(output))
            {
                // Use the raw output as-is (it contained tool calls but also may have text)
                messages.Add(new ChatMessage(ChatMessage.Assistant, output));
            }
            else
            {
                messages.Add(new ChatMessage(ChatMessage.Assistant, "..."));
            }

            // Inject tool results as Tool messages
            foreach (var r in toolResult.Results)
            {
                var toolContent = $"Tool result for {r.Name}:\n{r.Output}";
                if (r.Truncated)
                    toolContent += "\n\n[...truncated]";
                messages.Add(new ChatMessage(ChatMessage.Tool, toolContent));
            }

            // Image only valid for first generation
            imageBuffer = null;
        }

        // Dead letter: max iterations reached and all iterations produced tool calls.
        // Fall back to plain completion (no tool system prompt, no pipeline).
        // This prevents the model from looping endlessly on tool calls.
        if (!anyCleanOutput)
        {
            // Temporarily remove tool declarations so the model responds naturally
            var savedDeclarations = _toolDeclarations;
            _toolDeclarations = null;
            await foreach (var token in CompleteAsync(userMessage, imageBuffer, ct))
                yield return token;
            _toolDeclarations = savedDeclarations;
        }
        else
        {
            // We had some clean output but hit the iteration limit. Try one more time.
            var formattedPrompt = BuildPrompt(messages);
            await foreach (var token in InferRawAsync(formattedPrompt, imageBuffer, ct))
                yield return token;
        }
    }

    /// <summary>
    /// Raw inference using pre-built prompt text without BuildPrompt wrapping.
    /// Used by the tool loop to inject full conversation turns directly.
    /// </summary>
    private async IAsyncEnumerable<string> InferRawAsync(
        string rawPrompt,
        byte[]? imageBuffer,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);
        ObjectDisposedException.ThrowIf(_context is null, this);

        // Clear KV cache for the default sequence before each inference.
        _context.NativeHandle.MemorySequenceRemove(LLamaSeqId.Zero, (LLamaPos)0, (LLamaPos)int.MaxValue);

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
            AntiPrompts = CommonAntiPrompts,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _config.LlmTemperature,
                FrequencyPenalty = 0.0f,
                PresencePenalty = 0.0f,
                RepeatPenalty = 1.1f,
            },
        };

        var buffer = new System.Text.StringBuilder();

        await foreach (var token in executor.InferAsync(rawPrompt, inferenceParams, ct))
        {
            buffer.Append(token);
        }

        // Strip <think>...</think> blocks from output
        var fullOutput = buffer.ToString();
        while (true)
        {
            var start = fullOutput.IndexOf("<think", StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            var end = fullOutput.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                // Unterminated think block — strip from start to end
                fullOutput = fullOutput[..start];
                break;
            }
            fullOutput = fullOutput[..start] + fullOutput[(end + 8)..];
        }

        yield return fullOutput.Trim();
    }
}
