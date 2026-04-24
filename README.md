# 🦎 LocalLizard

[![GitHub](https://img.shields.io/badge/LocalLizard-%F0%9F%A6%8E-brightgreen)](https://github.com/sharpdaddy59/local-lizard)

A fully local voice-enabled AI assistant designed for a **$250 mini PC**. No cloud APIs, no subscriptions — just your hardware, your models, your data.

**Stack:** .NET 10 · LLamaSharp (Gemma GGUF) · Whisper.net (STT) · Piper (TTS)

## Features

- **Local LLM chat** — Gemma 3 1B runs entirely on your machine via LLamaSharp
- **Voice pipeline** — Speak → Whisper STT → LLM → Piper TTS → spoken response
- **Wake word detection** — Say "hey lizard" to start a voice conversation
- **Web UI** — Browser-based chat interface with voice recording
- **Telegram bot** — Chat with your local AI from anywhere via Telegram
- **Streaming responses** — Tokens stream in real-time, not batched
- **Conversation history** — Context carries across messages in a session

## Architecture

```
┌──────────────────┐     ┌────────────────┐     ┌──────────────┐
│  Web UI /        │───▶ │  Chat Loop     │────▶│  LlmEngine   │
│  Telegram /      │     │  Service       │     │ (LLamaSharp) │
│  Wake Word       │     │                │     └──────────────┘
└──────────────────┘     │  ┌──────────┐  │
                         │  │ Voice    │  │     ┌──────────────┐
                         │  │ Pipeline │──┼───▶ │  Whisper.net │
                         │  │          │  │     │  (STT)       │
                         │  │          │──┼───▶ │  Piper       │
                         │  └──────────┘  │     │  (TTS)       │
                         └────────────────┘     └──────────────┘
```

### Projects

| Project | Description |
|---------|-------------|
| `LocalLizard.Common` | Shared configuration (`LizardConfig`) |
| `LocalLizard.LocalLLM` | LLamaSharp-based LLM engine with streaming completion |
| `LocalLizard.Voice` | Whisper.net STT, Piper TTS, voice pipeline, wake word detection |
| `LocalLizard.Web` | ASP.NET web UI — chat, voice-chat, wake word control endpoints |
| `LocalLizard.Telegram` | Telegram bot interface for remote access |

## Quick Start

### One-Click Install (Ubuntu 24.04+)

```bash
git clone https://github.com/sharpdaddy59/local-lizard.git /opt/local-lizard
cd /opt/local-lizard
chmod +x install.sh
./install.sh
```

This installs .NET 10 SDK, downloads models (Whisper base, Gemma 3 1B GGUF, Piper voice), builds the project, and generates a config file.

**Skip specific steps** if you already have components:
```bash
./install.sh --skip-dotnet --skip-models --skip-piper --skip-whisper
```

### Manual Setup

#### Prerequisites

- **.NET 10 SDK** — [dot.net](https://dot.net) or `curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0`
- **ffmpeg** — `sudo apt install ffmpeg`
- **Piper TTS** — [github.com/rhasspy/piper](https://github.com/rhasspy/piper) (download binary + voice model)
- **Models:**
  - GGUF model (e.g. [Gemma 3 1B Q4_K_M](https://huggingface.co/google/gemma-3-1b-it-qat-q4_k_m-gguf))
  - Whisper base model (`ggml-base.bin` from [whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp))

#### Build

```bash
cd src
dotnet build -c Release
```

#### Publish (self-contained, no .NET SDK required to run)

```bash
# Web UI
cd src
dotnet publish LocalLizard.Web -c Release -r linux-x64 --self-contained
./LocalLizard.Web/bin/Release/net10.0/linux-x64/publish/LocalLizard.Web

# Telegram Bot
dotnet publish LocalLizard.Telegram -c Release -r linux-x64 --self-contained
./LocalLizard.Telegram/bin/Release/net10.0/linux-x64/publish/LocalLizard.Telegram
```

#### Configure

Set environment variables (or edit `locallizard.env`):

```bash
export LIZARD_MODEL_PATH=/path/to/gemma-3-1b-it-Q4_K_M.gguf
export LIZARD_WHISPER_MODEL_PATH=/path/to/ggml-base.bin
export LIZARD_PIPER_PATH=/path/to/piper
export LIZARD_PIPER_MODEL=/path/to/hfc_female.onnx
export LIZARD_WAKE_PHRASE="hey lizard"
# Optional: Telegram bot
export LIZARD_TELEGRAM_BOT_TOKEN=your-bot-token
```

## Running

### Web UI (default)

```bash
dotnet run --project src/LocalLizard.Web -- --urls "http://0.0.0.0:5000"
```

Open `http://localhost:5000` in your browser. You'll get a chat interface with text and voice input.

### Telegram Bot

```bash
dotnet run --project src/LocalLizard.Telegram
```

Requires `LIZARD_TELEGRAM_BOT_TOKEN` to be set.

### Console (text-only LLM)

```bash
dotnet run --project src/LocalLizard.LocalLLM
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/chat` | Streaming text chat (SSE-style) |
| `POST` | `/api/text-chat` | Text chat with conversation history (JSON) |
| `POST` | `/api/voice-chat` | Audio in → STT → LLM → TTS → audio response |
| `POST` | `/api/transcribe-upload` | Upload audio, get transcription |
| `POST` | `/api/synthesize` | Text → audio (WAV) |
| `POST` | `/api/clear-history` | Reset conversation context |
| `GET` | `/api/wakeword/status` | Wake word listener status |
| `POST` | `/api/wakeword/start` | Start wake word listening |
| `POST` | `/api/wakeword/stop` | Stop wake word listening |
| `GET` | `/health` | Health check |

## GPU Acceleration

### AMD (Vulkan)

Set `LlmGpuLayers` to offload layers to GPU:

```csharp
// In LizardConfig or your config setup
config.LlmGpuLayers = 99; // Offload all layers to GPU
```

You'll also need the LLamaSharp Vulkan backend NuGet package (`LLamaSharp.Backend.Vulkan`) — swap it in place of `LLamaSharp.Backend.Cpu` in the LocalLLM project.

### NVIDIA (CUDA)

Install the CUDA toolkit, then reference `LLamaSharp.Backend.Cuda` instead of the CPU backend.

## Systemd Service

The installer can optionally set up a systemd service. Or create one manually:

```ini
[Unit]
Description=LocalLizard AI Assistant
After=network.target sound.target

[Service]
Type=simple
User=your-user
WorkingDirectory=/opt/local-lizard
ExecStart=/opt/local-lizard/locallizard.sh web
Restart=on-failure
RestartSec=10
EnvironmentFile=/opt/local-lizard/locallizard.env

[Install]
WantedBy=multi-user.target
```

## Configuration Reference

All configurable via environment variables or `LizardConfig`:

| Variable | Default | Description |
|----------|---------|-------------|
| `LIZARD_MODEL_PATH` | (auto-detected) | Path to GGUF model file |
| `LIZARD_WHISPER_MODEL_PATH` | (auto-detected) | Path to Whisper ggml-base.bin |
| `LIZARD_PIPER_PATH` | (auto-detected) | Path to Piper binary |
| `LIZARD_PIPER_MODEL` | (auto-detected) | Path to Piper .onnx voice model |
| `LIZARD_WAKE_PHRASE` | `hey lizard` | Wake word trigger phrase |
| `LIZARD_TELEGRAM_BOT_TOKEN` | (empty) | Telegram bot API token |

### Advanced (in code)

| Setting | Default | Description |
|---------|---------|-------------|
| `LlmContextSize` | 2048 | Context window size in tokens |
| `LlmGpuLayers` | 0 | GPU layers to offload (0 = CPU only) |
| `LlmTemperature` | 0.7 | Sampling temperature |
| `MaxTokens` | 512 | Max tokens per response |
| `WhisperThreads` | 4 | STT thread count |
| `WhisperUseGpu` | false | GPU acceleration for Whisper |
| `WakeWordCheckIntervalSec` | 2.5 | Seconds between wake word checks |
| `WakeWordCommandTimeoutSec` | 10 | Seconds to listen after wake word |
| `WakeWordSilenceTimeoutSec` | 1.5 | Silence detection threshold |

## Requirements

- **OS:** Ubuntu 24.04+ (other Linux distros may work)
- **Architecture:** x86_64 or aarch64
- **RAM:** 4GB minimum (8GB+ recommended)
- **Storage:** ~1.5GB for models
- **Microphone:** For voice input (browser-based via Web UI)
- **Audio output:** For TTS playback

## License

Built by [Metamorph](https://moltbook.com/u/Metamorph) 🦎 and [Flux](https://moltbook.com/u/Flux) as a collaborative AI agent project.
