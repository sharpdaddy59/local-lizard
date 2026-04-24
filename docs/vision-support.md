# Vision / Multimodal Support

**Status:** Prepared but deferred. Vision code exists in `LlmEngine.cs` (using `InteractiveExecutor` with `MtmdWeights`) but is **disabled by default** and behind `Configuration.VisionEnabled = false`.

## What's Implemented

- `LlmEngine.CompleteAsync(string prompt, byte[]? imageBuffer, CancellationToken ct)` — optional image parameter
- `CanDoVision` property — reports whether mmproj loaded successfully
- `Configuration.MmprojPath`, `MtmdGpuLayers`, `MtmdThreads`, `VisionEnabled` — all present in config

## Why It's Deferred

Vision inference with Gemma 4 E2B requires **GPU-accelerated llama.cpp** for practical use. Here's why:

### The Clip Projector is BF16

The multimodal projector (`mmproj-gemma4-E2B-BF16.gguf`, 942 MB) stores all vision encoder weights as BF16 floats. The Ryzen 5 3550H (and most CPU-only mini PCs) does not support native BF16 instructions. Every matrix multiply in the clip encoder has to be **software-emulated**, making image encoding impractically slow — we observed SIGSEGV crashes and timeouts at 120+ seconds.

### Our LLamaSharp Build is CPU-Only

LLamaSharp's `deps/vulkan/` directory ships `libggml-vulkan.so`, but our custom `libllama.so` and `libmtmd.so` were built with only the CPU backend. The Vega 8 integrated GPU on brazos sits unused. Rebuilding with `-DGGML_VULKAN=ON` would solve this, but adds a build configuration step for users.

### The "No Separate Server" Constraint

The core LocalLizard design goal is single-process — users shouldn't start a server separately. In-process vision with `MtmdWeights` is architecturally correct, but the native interop between C# and the mtmd encoder is fragile. We hit segfaults at `encoding image slice...` that suggest edge cases in how LLamaSharp wraps the llama.cpp mtmd API.

## How to Enable Vision (Future)

If you have a GPU-capable system and want to try:

1. **Rebuild llama.cpp with Vulkan** (or CUDA/Metal for your platform):
   ```bash
   cd /path/to/local-lizard/native
   git clone https://github.com/ggml-org/llama.cpp
   cd llama.cpp
   cmake -B build -DGGML_VULKAN=ON
   cmake --build build --config Release -j
   ```

2. **Rebuild LLamaSharp native libs** with the updated llama.cpp submodule.

3. **Copy the GPU-backed .so files** to the output directory:
   - `libggml-vulkan.so` (or `libggml-cuda.so` / `libggml-metal.dylib`)
   - GPU-enabled `libllama.so` and `libmtmd.so`

4. **Set config:**
   ```json
   {
     "VisionEnabled": true,
     "MmprojPath": "/path/to/mmproj-gemma4-E2B-BF16.gguf",
     "MtmdGpuLayers": 99
   }
   ```

## Alternative Vision Models

The Gemma 4 E2B mmproj is large (942 MB BF16). Smaller clip projectors from other models may work on CPU:

| Model | Clip Size | Format | CPU-Friendly |
|-------|-----------|--------|-------------|
| LLaVA 1.6 | ~250 MB | F16/Q8 | ✅ |
| Qwen2.5-VL-7B | ~400 MB | BF16 | ⚠️ |
| Gemma 4 E2B | 942 MB | BF16 | ❌ |

A future migration to a model with a smaller or Q-quantized clip projector would make in-process vision on CPU practical.

## Technical Notes

- The vision pipeline uses `InteractiveExecutor(LLamaContext, MtmdWeights)` constructor
- Image is loaded via `_mtmd.LoadMedia(buffer.AsSpan())` and queued via `executor.Embeds.Add()`
- The `<media>` marker in the prompt is replaced with `<|image|>` and image tokens by LLamaSharp's mtmd preprocessor
- Fresh `LLamaContext` is created per vision call (same pattern as text-only to avoid `invalidInputBatch`)
- The `MtmdWeights` and `InteractiveExecutor` are non-`IDisposable` — the parent `LlmEngine` manages their lifetime
