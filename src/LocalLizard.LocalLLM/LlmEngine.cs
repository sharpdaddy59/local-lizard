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
    // Prompt format constants — initialized from config (Gemma 4 or ChatML)
    private readonly string _userPrefix;
    private readonly string _modelPrefix;
    private readonly string _turnSep;
    private readonly string _sysPrefix;
    private readonly string _antiPrompt;

    // GBNF grammar pipelines for tool calling
    private LLama.Sampling.ISamplingPipeline? _toolGrammarPipeline;      // Lazy grammar (triggered by <|tool_call|>)
    private LLama.Sampling.ISamplingPipeline? _postToolGrammarPipeline; // Constrains post-tool output

    private LLamaWeights? _model;
    private MtmdWeights? _mtmd;
    private LLamaContext? _context;
    private readonly LizardConfig _config;

    public LlmEngine(LizardConfig config)
    {
        _config = config;

        // Set prompt format based on config
        if (config.PromptFormat == "chatml")
        {
            _userPrefix = "<|im_start|>user\n";
            _modelPrefix = "<|im_start|>assistant\n";
            _turnSep = "<|im_end|>\n";
            _sysPrefix = "<|im_start|>system\n";
                _antiPrompt = "<|im_end|>";
        }
        else
        {
            // Default: Gemma 4 native format
            _userPrefix = "<|turn>user\n";
            _modelPrefix = "<|turn>model\n";
            _turnSep = "<turn|>\n";
            _sysPrefix = "<|turn>system\n";
            _antiPrompt = "<turn|>";
        }

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
        // errors. The KV cache is cleared before each inference using
        // MemorySequenceRemove, which is faster than recreating the context
        // and avoids the invalidInputBatch errors.
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

        // Create GBNF grammar pipeline for post-tool-inference output
        if (_config.ToolGrammarEnabled)
        {
            _toolGrammarPipeline = CreateToolGrammarPipeline();
            _postToolGrammarPipeline = CreatePostToolGrammarPipeline();
        }
    }

    private string _systemContext =>
        $"{_sysPrefix}You are a helpful voice assistant running on a mini PC. " +
        $"You can send text or voice replies. Keep responses conversational and concise.{_turnSep}";

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
        sb.Append(_userPrefix);
        sb.Append(userMessage.Trim());
        sb.Append(_turnSep);
        sb.Append(_modelPrefix);
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

        // Clear KV cache for the default sequence before each inference.
        // The context retains cached token positions between turns, which
        // causes invalidInputBatch errors if not cleared.
        // implicit operator int → LLamaPos exists in the assembly
        _context.NativeHandle.MemorySequenceRemove(LLamaSeqId.Zero, (LLamaPos)0, (LLamaPos)int.MaxValue);

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
            AntiPrompts = [_antiPrompt],
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
    private string _combinedSystemContext = "";

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
            _combinedSystemContext = _systemContext;
        }
        else
        {
            // Merge base system context and tool declarations into one &lt;|turn&gt;system block.
            // Gemma 4 expects a single system turn, not consecutive system blocks.
            var baseText = _systemContext.TrimEnd();
            // Remove trailing separator from baseText
            if (baseText.EndsWith(_turnSep.TrimEnd()))
                baseText = baseText[..^_turnSep.TrimEnd().Length];
            _combinedSystemContext = $"{_sysPrefix}{baseText}\n\n{prompt.Trim()}{_turnSep}";
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
    /// Tool results are injected using Gemma 4's native <|tool_response> format
    /// (not [TOOL_RESULT] brackets), continuing the model's turn inline.
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
        conversation.Append(_userPrefix);
        conversation.Append(userMessage.Trim());
        conversation.Append(_turnSep);
        conversation.Append(_modelPrefix);

        // Track whether any iteration produced clean (non-tool-call) output
        bool anyCleanOutput = false;

        for (int iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var outputBuilder = new System.Text.StringBuilder();
            var samplingPipeline = iteration == 0
                ? _toolGrammarPipeline
                : (_postToolGrammarPipeline ?? _toolGrammarPipeline);
            await foreach (var token in InferRawAsync(
                conversation.ToString(), imageBuffer, ct,
                overrideSampling: samplingPipeline))
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
            conversation.Append(_turnSep);

            // Inject tool results.
            // For ChatML, inject as a user turn (the model expects function results
            // to come from the user in standard chat format).
            // For Gemma 4, inject as <|tool_response> blocks continuing the model turn.
            if (_config.PromptFormat == "chatml")
            {
                // End the assistant turn
                conversation.Append(_turnSep);

                // Inject each tool result as a user message
                foreach (var r in toolResult.Results)
                {
                    conversation.Append(_userPrefix);
                    conversation.Append($"Tool result for {r.Name}:\n{r.Output}");
                    conversation.Append(_turnSep);
                }

                // Start next assistant turn
                conversation.Append(_modelPrefix);
            }
            else
            {
                // Gemma 4: inject as <|tool_response> blocks continuing the model turn
                foreach (var r in toolResult.Results)
                {
                    conversation.Append(ToolCallParser.FormatResult(r.Name, r.Status, r.Output, r.Truncated));
                }
                conversation.Append(_turnSep);

                // Continue the model turn
                conversation.Append(_modelPrefix);
            }

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
        [EnumeratorCancellation] CancellationToken ct,
        LLama.Sampling.ISamplingPipeline? overrideSampling = null)
    {
        ObjectDisposedException.ThrowIf(_model is null, this);
        ObjectDisposedException.ThrowIf(_context is null, this);

        // Clear KV cache for the default sequence before each inference.
        // implicit operator int → LLamaPos exists in the assembly
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
            AntiPrompts = [_antiPrompt],
            SamplingPipeline = overrideSampling ?? new DefaultSamplingPipeline(),
        };

        await foreach (var token in executor.InferAsync(rawPrompt, inferenceParams, ct))
        {
            yield return token;
        }
    }

    /// <summary>
    /// Find the grammar.gbnf file by searching likely locations.
    /// Returns null if not found.
    /// </summary>
    /// <summary>
    /// Find a grammar.gbnf file by searching likely locations relative to the
    /// assembly base directory.
    /// </summary>
    private static string? FindGrammarFile(string filename)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", filename),
            Path.Combine(AppContext.BaseDirectory, filename),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Load the GBNF grammar text from the named grammar file. Returns the GBNF
    /// string and root rule name. Returns null if the file doesn't exist, is
    /// empty, or starts with a comment (##) marking it as intentionally empty.
    /// </summary>
    private static (string gbnf, string root)? LoadGrammar(string filename)
    {
        var path = FindGrammarFile(filename);
        if (path is null)
            return null;

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Comment-only or intentionally-empty files start with ##
        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.StartsWith("##"))
            return null;

        return (text, "root");
    }

    /// <summary>
    /// Create a lazy GBNF grammar pipeline for tool call generation.
    /// Uses lazy grammar sampling: free text flows normally until the
    /// &lt;|tool_call|&gt; pattern is matched, then grammar constrains to valid format.
    /// Returns null if the grammar file doesn't exist or fails to compile.
    /// </summary>
    private LLama.Sampling.ISamplingPipeline? CreateToolGrammarPipeline()
    {
        ObjectDisposedException.ThrowIf(_model is null, this);

        var loaded = LoadGrammar("tool-call.gbnf") ?? LoadGrammar("grammar.gbnf");
        if (loaded is null)
            return null;

        try
        {
            return new LazyToolGrammarPipeline(
                _model.NativeHandle,
                loaded.Value.gbnf,
                loaded.Value.root,
                triggerPatterns: ["<|tool_call|>call:"],
                temperature: _config.LlmTemperature);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create tool grammar pipeline: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create a GBNF grammar pipeline for post-tool-inference output.
    /// Uses standard grammar with Basic optimization to prevent recursive tool calls.
    /// Returns null if the grammar file doesn't exist or fails to compile.
    /// </summary>
    private LLama.Sampling.ISamplingPipeline? CreatePostToolGrammarPipeline()
    {
        if (_model is null)
            return null;

        var loaded = LoadGrammar("post-tool.gbnf");
        if (loaded is null)
            return null;

        try
        {
            return new LLama.Sampling.DefaultSamplingPipeline
            {
                Grammar = new LLama.Sampling.Grammar(loaded.Value.gbnf, loaded.Value.root),
                GrammarOptimization = LLama.Sampling.DefaultSamplingPipeline.GrammarOptimizationMode.Basic,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load GBNF grammar: {ex.Message}");
            return null;
        }
    }
}
