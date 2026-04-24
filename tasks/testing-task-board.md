# LocalLizard Testing — Task Board

## In Progress

### T6: Full voice loop integration
- **Status:** Needs Wilywit — requires mic/speaker setup on brazos via RDP
- WakeWordService + VoicePipeline code reviewed, looks correct
- Individual components (STT, LLM, TTS) all verified working

### T7: Telegram bot test
- **Status:** Needs bot token from Wilywit (set LIZARD_TELEGRAM_BOT_TOKEN env var)
- Bot code reviewed — full feature set: text/voice messages, commands, conversation history
- Supports OGG voice notes via ffmpeg conversion

## Pending

### T2: LLM component test ✅
- **Completed:** 2026-04-22 23:15 CDT
- Gemma 1B loaded, correctly answered "2+2=4" via LLamaSharp chat session

### T3: STT component test ✅
- **Completed:** 2026-04-22 23:25 CDT (after resampling fix)
- Fixed audio sample rate mismatch: Piper outputs 22050 Hz, Whisper.net requires 16KHz
- Added ffmpeg resampling step in WhisperSTTService.Ensure16KhzWavAsync()
- Transcribed "The quick brown fox jumps over the lazy dog" — word perfect

### T4: TTS component test ✅
- **Completed:** 2026-04-22 23:20 CDT
- Piper generated 165KB WAV (22050 Hz mono 16-bit PCM)
- Model: `en_US-hfc_female-medium.onnx` (63.2 MB)
- Config path fixed to match actual file

### T5: Web UI smoke test ✅
- **Completed:** 2026-04-22 23:30 CDT
- ASP.NET server started on localhost:5190
- Health endpoint responding
- Chat API streaming responses: "What is the capital of France?" → "Paris"
- Full API surface: /health, /api/chat, /api/voice-chat, /api/transcribe-upload, /api/synthesize, /api/wakeword/*

### T5: Web UI smoke test
- Start ASP.NET server
- Verify chat page loads in browser
- Send a message through the web interface

### T6: Full voice loop integration
- Connect wake word → STT → LLM → TTS end-to-end
- Test with mic/speaker on brazos (needs Wilywit for physical setup)

### T7: Telegram bot test
- Configure bot token
- Send message via Telegram, verify LLM response comes back

### T8: Bug triage & fixes
#### Bug 1: Audio sample rate mismatch (Critical)
- Piper outputs 22050 Hz, Whisper.net requires 16000 Hz
- Fix options: (a) ffmpeg resampling step, (b) NAudio/SkiaSharp resampler in C#, (c) Piper `--output-raw` + custom WAV header at 16KHz
- Recommended: ffmpeg as intermediate step — already available on target platform

#### Bug 2: Config path mismatch ✅ FIXED
- PiperModel default updated to `en_US-hfc_female-medium.onnx`
- WhisperModelPath default updated to `/home/wily/dev/whisper.cpp/models/ggml-base.en.bin`

#### Bug 3: Whisper model path mismatch ✅ FIXED (merged with Bug 2)

## Completed

### T1: Compile check & fix build errors ✅
- **Completed:** 2026-04-22 23:10 CDT
- `dotnet build` succeeded — 0 warnings, 0 errors, all 4 projects compiled
- LocalLizard.Common, LocalLizard.Voice, LocalLizard.LocalLLM, LocalLizard.Web all built clean

## Notes
- All 12 original build tasks are complete (Apr 17-22)
- This is the integration testing phase
- Two agents wrote code independently — expect integration friction
- Dotnet 10.0.106 available on brazos
