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
| Physical mic capture + always-listening loop | ✅ Phase 0 complete |
| VAD / wake word | ❌ Not started (Phase 3) |
| Camera brightness gate | ✅ Tested — cover open/closed detected correctly, 94x difference |

## Phases

### Phase 0 — Audio Capture Pipeline
*Goal: Reliable mic capture from C#, ready to feed into STT*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V0.1 | RC08 mic capture test (arecord/ffmpeg) | ✅ | 16KHz mono S16_LE verified on brazos headless. Mic gain set to 50% (35dB) — balances voice pickup vs background noise (A/C). Capture gain at 100% was causing excessive ambient noise. |
| V0.2 | C# audio capture via ALSA P/Invoke | ✅ (`534ba73`) | `AlsaCapture` — P/Invoke wrapper around `libasound.so.2`. `ReadAsync()`, `ReadUntilSilenceAsync()` (energy VAD), xrun recovery. |
| V0.3 | Buffering & chunking for STT | ✅ (`380b6b2`) | `ReadUntilSilenceAsync` (50ms chunks + energy VAD) built into AlsaCapture. `CaptureAndTranscribeAsync` wires into VoicePipeline. `PcmToWavStream` helper for Whisper. |
| V0.4 | Recording trigger mechanism | ✅ (`f60be5f`) | BrightnessGate (camera cover = privacy toggle). Always-listening loop with infrequent brightness checks (5s interval) to minimize LED blinking. Min audio filter (500ms) rejects noise. |

### Phase 1 — Speech-to-Text Pipeline
*Goal: Whisper STT integrated into C# pipeline with minimal latency*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V1.1 | Whisper integration approach | ✅ | Whisper.net P/Invoke bindings already wired in `CaptureAndTranscribeAsync`. 3-strike fallback to subprocess. No separate daemon needed. |
| V1.2 | Wire STT into voice pipeline | ✅ | `CaptureAndTranscribeAsync()` already connects mic capture → WAV → Whisper → text. Same path as Telegram voice. |
| V1.3 | Warm-start model | ❌ | Keep whisper model loaded between turns to save 1-2s per interaction. Relevant for Phase 2 latency. |
| V1.4 | STT error handling & logging | ❌ | Bad audio, empty transcript, model load failures |

### Phase 2 — Physical Voice Loop (Always-Listening)
*Goal: Mic → VAD → STT → Router/LLM → TTS → Speaker, on-device*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V2.1 | Wire audio capture → STT | ✅ (`380b6b2`) | `CaptureAndTranscribeAsync()` already connects mic → WAV → Whisper → text. `StartListeningLoopAsync()` provides continuous loop with brightness gate. |
| V2.2 | Wire LLM output → Piper TTS → aplay | ✅ (`b32afd9`) | `SpeakAsync()` pipes Piper → aplay (zero temp files). `StartConversationLoopAsync(onHeard)` = full listen → respond → speak loop. Test harness has echo responder for hardware validation. |
| V2.3 | End-to-end smoke test (speak → hear reply via RC08) | ✅ | **Proven Apr 27.** Test 6: heard "Yes I can actually hear you very well" → echoed via TTS. Test 7: conversation loop heard + responded via speaker. Wily confirmed hearing both. |
| V2.4 | Latency measurement & logging | ❌ | Target: <5s total for deterministic intents. shm for WAV files avoids disk wear (Flux's suggestion). |
| V2.5 | Volume normalization (input & output) | ❌ | Consistent levels regardless of mic distance |
| V2.6 | Cold-start silence detection fix | ✅ | Three-state VAD machine implemented. `ReadUntilSilenceVadAsync` in AlsaCapture. Hard timeout 12s, speech endpoint 1.5s, min speech 300ms. Code reviewed by Metamorph, two bugs caught and fixed by Flux (minSpeechMs enforcement, partial return on hard timeout). Hardware tested Apr 28 — test 6 and test 7 both pass. |

### Phase 3 — Interaction Design
*Goal: Natural-feeling voice interaction*

| # | Task | Status | Notes |
|---|------|--------|-------|
| V3.1 | Push-to-talk vs always-listening decision | ❌ | **For headless box: always-listening is default.** Telegram voice is push-to-talk (already works). Physical interaction needs VAD loop. |
| V3.2 | Voice Activity Detection (VAD) | ✅ | `EnergyVad` class with `IVoiceActivityDetector` interface. RMS energy in dBFS, default -35 dBFS threshold. Configurable via `VadThresholdDb`. Interface-based — webrtcvad can slot in later. Hardware tested Apr 28. Note: -35 dBFS catches keyboard clicking in quiet rooms; may need tuning to -38/-40 when closer to desk. |
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
- **M2:** Full voice loop works end-to-end via RC08 ✅ (Apr 27 — test 7 conversation loop)
- **M3:** Sub-5s latency on deterministic intents ❌ (Phase 2)
- **M4:** Voice pipeline reliable enough for daily use ❌ (Phase 4)

## Known Issues

- **VAD threshold sensitivity:** At -35 dBFS, keyboard clicking can trigger voice detection in quiet rooms (no AC). May need tuning to -38 or -40 dBFS for desk-proximity use. Not blocking — configurable via `VadThresholdDb`.

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

- **Mic:** RC08 webcam, ALSA Card 1, Capture at 50% gain (35dB), 16KHz mono S16_LE. Mic activity does NOT light the LED.
- **Speaker:** RC08 webcam, ALSA Card 1, PCM at 75%, 48KHz (Piper resamples from 22050Hz). Speaker activity lights blue LED.
- **LED:** Blue = camera or speaker active. Red = cover closed (hardware privacy indicator). Off = idle. Mic capture does NOT trigger LED.
- **GPU:** Radeon Vega (Vulkan for whisper.cpp, 1.81x CPU speed)
- **Card 2:** CX20632 Analog (motherboard) — headphone jack only, no built-in speaker
- **Audio workaround:** `sg audio -c` for subprocess calls. P/Invoke path requires permanent `audio` group (logout/login needed).

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
- `/shared/projects/local-lizard/docs/vad-coldstart-proposal.md`
- Current Telegram bot: `src/LocalLizard.Telegram/BotService.cs` (voice pipeline already wired)
- Voice library: `src/LocalLizard.Voice/` (WhisperSTTService, PiperTTSService, VoicePipeline)
