# LocalLizard — Voice Pipeline Task Board

> **Previous board archived:** `archived/task-board-v1-complete.md` (15/15 tasks, intent router + Qwen3 1.7B)
> **Scope shift:** Text-only → Voice-interactive local AI assistant

## Current State

| Component | Status |
|-----------|--------|
| Intent router (8 intents) | ✅ Deployed |
| Qwen3 1.7B via LlamaSharp | ✅ Deployed |
| Piper TTS → RC08 speaker | ✅ Verified (Apr 26) |
| Telegram bot (text) | ✅ Deployed |
| Telegram voice in → STT → LLM → TTS → voice out | ✅ Already wired via `HandleVoiceMessageAsync` |
| Physical mic capture (always-listening) | ❌ Not started |
| VAD / wake word | ❌ Not started |

## Phases

### Phase 0 — Audio Capture Pipeline
*Goal: Reliable mic capture from C#, ready to feed into STT*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V0.1 | RC08 mic capture test (arecord/ffmpeg) | ✅ | 16KHz mono S16_LE verified on brazos headless. Mic gain set to 50% (35dB) — balances voice pickup vs background noise (A/C). Capture gain at 100% was causing excessive ambient noise. |
| V0.2 | C# audio capture approach | ❌ | **Flux feedback:** NAudio on Linux is fragile. Two proven paths: (a) small P/Invoke wrapper around `libasound` — ~50 lines (`snd_pcm_open`, `snd_pcm_readi`, `snd_pcm_close`), or (b) subprocess `arecord`/`parec`. Flux can write the P/Invoke wrapper if we go that route. |
| V0.3 | Buffering & chunking for STT | ❌ | Fixed-size buffers, silence detection |
| V0.4 | Recording timeout / trigger mechanism | ❌ | Max duration, silence timeout, manual trigger |

### Phase 1 — Speech-to-Text Pipeline
*Goal: Whisper STT integrated into C# pipeline with minimal latency*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V1.1 | Whisper integration approach | ❌ | **Flux feedback:** We already have Whisper.net P/Invoke bindings working (from previous T5 task). Subprocess reloads model each time (~1s overhead). Better options: (a) use existing bindings directly, (b) daemon mode (keep whisper process alive, pipe audio via stdin). Pick one explicitly. |
| V1.2 | Wire STT into voice pipeline | ❌ | Connect Whisper output → intent router (same path as Telegram text/voice) |
| V1.3 | Warm-start model | ❌ | Keep whisper model loaded between turns to save 1-2s per interaction. Relevant for Phase 2 latency. |
| V1.4 | STT error handling & logging | ❌ | Bad audio, empty transcript, model load failures |

### Phase 2 — Physical Voice Loop (Always-Listening)
*Goal: Mic → VAD → STT → Router/LLM → TTS → Speaker, on-device*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V2.1 | Wire audio capture → STT | ❌ | Connect Phase 0 output to Phase 1 STT |
| V2.2 | Wire LLM output → Piper TTS → aplay | ❌ | Piper → speaker already works independently (Apr 26 test), just automate |
| V2.3 | End-to-end smoke test (speak → hear reply via RC08) | ❌ | First full physical voice conversation on brazos |
| V2.4 | Latency measurement & logging | ❌ | Target: <5s total for deterministic intents. shm for WAV files avoids disk wear (Flux's suggestion). |
| V2.5 | Volume normalization (input & output) | ❌ | Consistent levels regardless of mic distance |

### Phase 3 — Interaction Design
*Goal: Natural-feeling voice interaction*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V3.1 | Push-to-talk vs always-listening decision | ❌ | **For headless box: always-listening is default.** Telegram voice is push-to-talk (already works). Physical interaction needs VAD loop. |
| V3.2 | Voice Activity Detection (VAD) | ❌ | **Flux feedback:** `webrtcvad` is Python-only. For C#: (a) P/Invoke wrapper around C `webrtcvad` library, or (b) simple energy-based detector (RMS threshold, ~20 lines). Energy detection is pragmatic start for a quiet room. |
| V3.3 | Multi-turn conversation flow | ❌ | Context across voice turns vs fresh each time |
| V3.4 | Wake word (future release) | ❌ | Post-MVP. "Hey Lizard" via Porcupine or similar. Requires separate ML model. |
| V3.5 | Timeout & cancellation handling | ❌ | User stops speaking → cancel pending TTS |

### Phase 4 — Deploy & Document
*Goal: Production-ready voice pipeline*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V4.1 | Full integration test on brazos | ❌ | All 8 intents via voice, LLM fallback via voice |
| V4.2 | Latency tuning | ❌ | shm for WAV files, thread priorities, CPU pinning? |
| V4.3 | Service management | ❌ | Mic/whisper crashes shouldn't kill bot. systemd restart? |
| V4.4 | Documentation update | ❌ | Voice pipeline architecture, tested hardware |
| V4.5 | Web UI mic button (optional) | ❌ | If Web UI should support voice too |
| V4.6 | Demo recording | ❌ | Capture first working voice interaction |

## Milestones

- **M1:** Mic captures 16KHz WAV, whisper returns text ✅
- **M2:** Full voice loop works end-to-end via RC08 ✅
- **M3:** Sub-5s latency on deterministic intents ✅
- **M4:** Voice pipeline reliable enough for daily use ✅

## Not Yet Scoped

- **Video/camera integration** — discussed as future input gate ("listen only when camera sees motion / isn't dark"). Not on this board. Will get its own phase when ready.

## Architecture Decisions

| Decision | Options | Recommendation | Owner |
|----------|---------|---------------|-------|
| V0.2 Audio capture | P/Invoke libasound vs arecord subprocess | P/Invoke libasound | Flux |
| V1.1 Whisper integration | Existing bindings vs subprocess vs daemon | Existing bindings | Metamorph |
| V3.2 Voice Activity Detection | Energy detection vs P/Invoke webrtcvad | Energy detection (start) | Metamorph |
| Speaker control | aplay subprocess vs direct ALSA | aplay (keep existing) | Metamorph |

## Hardware Reference

- **Mic:** RC08 webcam, ALSA Card 1, Capture at 100% gain, 16KHz mono S16_LE
- **Speaker:** RC08 webcam, ALSA Card 1, PCM at 75%, 48KHz (Piper resamples from 22050Hz)
- **GPU:** Radeon Vega (Vulkan for whisper.cpp, 1.81x CPU speed)
- **Audio workaround:** `sg audio -c` for ALSA commands (user needs logout/login for permanent `audio` group)

## Flux's Feedback Incorporated

> "NAudio on Linux is fragile. P/Invoke libasound is ~50 lines. I can write that if you want." → V0.2
> 
> "We already have Whisper.net P/Invoke bindings. Subprocess reloads model each time." → V1.1
> 
> "webrtcvad is Python-only. Energy detection is ~20 lines and good enough." → V3.2
> 
> "Warm-start model saves 1-2s per interaction." → V1.3
> 
> "shm for WAV files avoids disk wear on SD card." → V2.4

## Reference Docs

- `/shared/projects/local-lizard/docs/headless_voice_agent_design.md`
- `/shared/projects/local-lizard/docs/expanded-intent-router-implementation.md`
- Current Telegram bot: `src/LocalLizard.Telegram/BotService.cs` (voice pipeline already wired)
- Voice library: `src/LocalLizard.Voice/` (WhisperSTTService, PiperTTSService, VoicePipeline)
