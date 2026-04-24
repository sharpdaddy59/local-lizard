# LocalLizard Hardware Compatibility List

*Last updated: 2026-04-19*

## Tested Configurations

### ✅ brazos — AMD Ryzen 5 3550H Mini PC

| Component | Details |
|-----------|---------|
| CPU | AMD Ryzen 5 3550H (4C/8T) |
| RAM | 32 GB DDR4 |
| GPU | AMD Radeon Vega Mobile (Picasso) |
| Storage | 469 GB NVMe SSD |
| OS | Kubuntu 24.04.3 LTS (kernel 6.17) |

**Status:** Full compatibility — primary development machine.

- **LLM (LLamaSharp):** Runs Gemma 3-1B Q4_K_M comfortably. Larger models (7B Q4) feasible with 32 GB RAM but slower.
- **STT (whisper.cpp):** Vulkan GPU acceleration active (~1.8x over CPU). `base.en` model real-time capable.
- **TTS (piper):** CPU-only, negligible resource usage.
- **Wake word:** Always-listening mode tested — no perceptible system impact.
- **Audio:** No physical audio devices (headless). Works with virtual/null audio for processing.

### ✅ aransas — AMD Ryzen 7 5825U Mini PC

| Component | Details |
|-----------|---------|
| CPU | AMD Ryzen 7 5825U (8C/16T) |
| RAM | 32 GB DDR4 |
| GPU | AMD Radeon (Barcelo) |
| OS | Linux 6.17 (Ubuntu-based) |

**Status:** Compatible — more powerful than brazos, should run everything faster.

- Not yet running LocalLizard, but hardware profile supports all components.

---

## Component Requirements

| Component | Min RAM | Recommended RAM | GPU | Notes |
|-----------|---------|-----------------|-----|-------|
| .NET 8 Runtime | 512 MB | 1 GB | None | |
| LLamaSharp (1B Q4) | 2 GB | 4 GB | Optional (Vulkan) | CPU inference works fine |
| LLamaSharp (7B Q4) | 8 GB | 16 GB | Optional (Vulkan) | Noticeably slower without GPU |
| whisper.cpp | 1 GB | 2 GB | AMD Vulkan recommended | base.en model; GPU ~1.8x faster |
| piper | 256 MB | 512 MB | None | Lightweight |
| ASP.NET Web UI | 256 MB | 512 MB | None | Minimal overhead |

### Minimum Viable System
- **CPU:** 4 cores (AMD or Intel, x86_64)
- **RAM:** 8 GB (for 1B model; 16 GB recommended for 7B)
- **Storage:** 10 GB free (models + app)
- **GPU:** Not required. AMD Radeon Vega or later with Vulkan support improves whisper.cpp performance significantly.
- **OS:** Ubuntu 22.04+ or similar Linux distro with kernel 5.15+
- **.NET:** 8.0 SDK/Runtime

## Known Limitations

- **NVIDIA GPUs:** Not tested. LLamaSharp supports CUDA; whisper.cpp supports CUDA. Should work but unverified.
- **ARM64:** Not tested. LLamaSharp supports ARM64 on Linux but not verified with this stack.
- **Audio hardware:** Headless machines need virtual audio (PulseAudio null sink or PipeWire) for full voice pipeline testing.
- **Raspberry Pi:** Likely too resource-constrained for on-device LLM inference, even with 8 GB models. Could work as a thin client connecting to a remote LocalLizard instance.

## Recommended Hardware Tiers

| Tier | Specs | Expected Performance |
|------|-------|---------------------|
| **Budget** | Ryzen 3 / i3, 16 GB RAM, NVMe | 1B model: responsive. 7B: slow (~2-3 tok/s) |
| **Sweet Spot** | Ryzen 5 / i5, 32 GB RAM, NVMe | 1B: instant. 7B: usable (~5-8 tok/s) |
| **Power** | Ryzen 7+ / i7+, 32+ GB RAM, NVMe + dGPU | 7B: fast. 13B: feasible. |

---

*Contributors: Metamorph*
*Test hardware provided by Wilywit*
