# LLM Integration — How LocalLizard Talks to llama.cpp

LocalLizard uses **LLamaSharp** — a C# binding over llama.cpp's native shared library — to run GGUF models in-process. No sidecar server, no HTTP calls, no Ollama. This document explains the custom integration.

## Architecture

```
┌─────────────────────┐
│   Your C# Code      │
│  (LlmEngine.cs)     │
│  CompleteAsync()    │
├─────────────────────┤
│   LLamaSharp.dll    │  ← C# library (NuGet, but custom-built)
├─────────────────────┤
│   libllama.so       │  ← llama.cpp native (custom-built from source)
│   libggml*.so       │
└─────────────────────┘
```

Everything lives in one process. The model weights (`LLamaWeights`) are loaded once and shared. Contexts are created per-request.

## Prompt Format: The Hard Way

### The Problem

Gemma 4 ships with a **3.6 KB multimodal Jinja chat template** embedded in the GGUF metadata (`tokenizer.chat_template`). This template is what `llama_chat_apply_template()` uses to format messages. It looks roughly like:

```
{{ bos_token }}
{%- if messages[0]['role'] == 'system' -%}
  {%- if add_generation_prompt -%}
    ... ~150 lines of Jinja with media parts, tool calls, multi-modal ...
  {%- endif -%}
{%- endif -%}
```

LLamaSharp 0.26.0 wraps this in a class called `PromptTemplateTransformer` / `LLamaTemplate`. When you call `ChatSession.ChatAsync()`, it:

1. Takes your `ChatHistory` messages
2. Calls `llama_chat_apply_template(native_ptr, template_string, messages_json, ...)`
3. Passes the formatted prompt to the executor

**This crashes with Gemma 4.** The internal `llama_chat_apply_template` implementation chokes on Gemma 4's Jinja template — returns `MissingTemplateException: template not found`.

### The Fix: Manual Prompt Format

Instead of the template system, we build prompts by hand using Gemma 4's simple fallback format:

```
<|turn>system
You are LocalLizard, a helpful voice assistant...
<turn|>
<|turn>user
What is 2+2?
<turn|>
<|turn>model

```

**Rules:**
- **No `<bos>` prefix.** The GGUF has `add_bos_token = true`. The tokenizer prepends BOS automatically.
- **Anti-prompt for stopping:** `<turn|>` is set as the anti-prompt so generation stops before the model starts a new turn.
- **System context** goes in a `<|turn>system\n...<turn|>\n` block at the start. Every completion gets it injected.
- **No conversation history.** Each call is a brand-new prompt with just the system context + the user's single message.

### Key code (`LlmEngine.cs`)

```csharp
private const string UserPrefix = "<|turn>user\n";
private const string ModelPrefix = "<|turn>model\n";
private const string TurnSep = "<turn|>\n";
private const string SysPrefix = "<|turn>system\n";

private static readonly string SystemContext =
    $"{SysPrefix}You are LocalLizard, a helpful voice assistant. " +
    $"You can send text or voice replies. Keep responses conversational and concise.{TurnSep}";

private static string BuildPrompt(string userMessage)
    => $"{SystemContext}{UserPrefix}{userMessage.Trim()}{TurnSep}{ModelPrefix}";
```

Then fed to raw `InteractiveExecutor.InferAsync()`:

```csharp
var executor = new InteractiveExecutor(context);
var inferenceParams = new InferenceParams
{
    MaxTokens = _config.MaxTokens,
    AntiPrompts = ["<turn|>"],
    SamplingPipeline = new DefaultSamplingPipeline(),
};

await foreach (var text in executor.InferAsync(formattedPrompt, inferenceParams, ct))
    yield return text;
```

## Config Settings (`Configuration.cs`)

| Setting | Default | Notes |
|---------|---------|-------|
| `ModelPath` | `gemma-4-E2B-it-Q4_K_M.gguf` | Full path on brazos |
| `LlmContextSize` | 4096 | Fits most turns. Gemma 4 E2B supports up to 1M tokens context |
| `LlmGpuLayers` | 0 | CPU-only on the mini PC. Set to 99 for GPU offload |
| `LlmTemperature` | 0.7 | Default sampling temp |
| `MaxTokens` | 512 | Response length limit. E2B is a thinking model — needs 512+ for long answers |
| `WhisperThreads` | 4 | Threads for whisper.cpp |
| `WhisperUseGpu` | false | GPU not needed for base.en model |

## State & Memory: The Current Situation

**There is none.** Each call to `CompleteAsync()`:

1. Creates a **fresh `LLamaContext`** from the shared model weights
2. Builds a prompt with system context + the user's single message
3. Runs inference
4. Disposes the context

This means:
- **No conversation history** — the model doesn't remember what you said before
- **No KV cache reuse** — every call re-processes the full prompt from scratch
- **No state management** — the `BotService.cs` *does* store a `Dictionary<long, List<(string Role, string Content)>>` of message history, but it's only for the `/history` command — it never feeds it back to the model

### Why No KV Cache Persistence?

The original design tried to share one `LLamaContext` across calls. This caused `invalidInputBatch` errors from llama.cpp on the second call — the KV cache state was stale/conflicting.

The fix was to create a fresh context per completion:

```csharp
using var context = _model.CreateContext(parameters);
var executor = new InteractiveExecutor(context);
```

This works but means:
- The model re-tokenizes and re-evaluates everything each time
- No efficient multi-turn conversations
- Higher latency per turn (~3-5 seconds for Gemma 4 E2B on Ryzen 5)

### How to Add Real Conversation Memory

There are three approaches, in increasing order of complexity:

**Option 1: Build multi-turn into the prompt (easiest)**
Instead of one user message, build a prompt with full history:
```
<|turn>system
You are LocalLizard...
<turn|>
<|turn>user
Hi
<turn|>
<|turn>model
Hello!
<turn|>
<|turn>user
What was my name?
<turn|>
<|turn>model

```

This is token-inefficient (re-processes all history every time) but works with the current architecture.

**Option 2: Keep the KV cache alive**
Don't dispose the context between turns. Instead, extend the existing context with new tokens. This requires tracking context position and carefully managing the KV cache. It's what `InteractiveExecutor` was designed for, but it broke with Gemma 4. Worth revisiting as LLamaSharp upstream matures.

**Option 3: Stateful conversation service**
Wrap the model in a service that manages context lifecycle — keep the context alive for N seconds of inactivity, then dispose. This is what production chat systems do.

## The Custom LLamaSharp Build

Since LLamaSharp 0.26.0 (Feb 2026) doesn't support Gemma 4's architecture, we build from source:

1. Clone LLamaSharp
2. Update the `llama.cpp` submodule to a commit that has `gemma4` support
3. Build the native libraries with `cmake`
4. Build the C# solution (`dotnet build`)
5. Package the native .so files and the C# NuGet package

This is automated in `scripts/build-local-llamasharp.sh`. The output:
- Native libs go in `runtimes/linux-x64/native/` (symlinks for soname resolution)
- The C# NuGet package goes in `packages/`
- `nuget.config` points to the local feed

## Native Library Resolution

At runtime, llama.cpp native libraries are loaded via `LD_LIBRARY_PATH` or by being present alongside the executable. The `CopyCustomNativeLibsToOutput` MSBuild target in each `.csproj` copies:

```
runtimes/linux-x64/native/avx2/libllama.so → $(OutputPath)/libllama.so
runtimes/linux-x64/native/avx2/libggml-cpu.so → $(OutputPath)/libggml-cpu.so
... etc
```

For `dotnet publish`, a post-publish `cp` is needed since the MSBuild target doesn't fire on Publish (see below).

## Known Limitations

| Issue | Status |
|-------|--------|
| `PromptTemplateTransformer` crashes with Gemma 4 | Working around with manual format |
| No multi-turn conversation memory | Not implemented |
| `CopyCustomNativeLibsToOutput` doesn't fire on `dotnet publish` | Manual `cp` after publish |
| Vision pipeline not tested in-process | Only tested via llama-server API |
| Fresh context per call = slower per turn | Acceptable for now |
| No system prompt override from config | Hardcoded in `LlmEngine.cs` |

## Testing

To verify the local build is working:

```bash
cd src/LocalLizard.LocalLLM
dotnet run -c Release
# Enter text, see responses
```

The console test (`Program.cs`) runs a simple read-eval loop using the same `LlmEngine` that the Telegram and Web projects use.
