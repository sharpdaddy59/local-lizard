# LocalLizard — Current Status

## Where We Are

All 15 original tasks (T1–T15) complete. Shifting to the **Headless Voice Agent** architecture.

## Migrated

| Phase | What | Status | Notes |
|-------|------|--------|-------|
| Phase 0 | Qwen 2.5 3B Q4_K_M downloaded + config swap | ✅ (Apr 26) | 1.8GB, 5-8 tok/s on brazos |
| Phase 1a | GBNF lazy grammar + ChatML prompt format | ✅ (`afbd2f7`) | Grammatically-correct tool calls, no retries |
| Phase 1b | Deterministic intent router (5 intents) | ✅ (`2937ee9`) | get_time: 0.0ms; remember/lookup routed via intent |
| Phase 1c | Schema fix (StartsWith → Contains) | ✅ (`2937ee9`) | All tools now pass correct parameter schemas |
| Phase 1d | Think block suppression + base prompt | ✅ (`2937ee9`) | Qwen3 1.7B/Qwen3.5 4B thinking blocked |
| Phase 1e | Single-arg remember_fact / lookup_fact | ✅ (`2937ee9`) | memory: / query: natural language arguments |
| Phase 1f | Qwen3 1.7B as default model | ✅ (`2937ee9`) | 1.1GB, ~3x faster than Qwen 2.5 3B |
| Phase 2a | Router cleanup: possessive-only lookup, structured JSON handlers, cascade (null → next intent) | ✅ (`f11b6bf`) | Fixes false positives on "what is X" math queries |
| Phase 2b | New intents: weather, math (bc), broad web search | ✅ (`9b3fda6`) | 134 lines added, 51/51 tests |
| Phase 2c | Bugfix: bc trailing newline (WriteLineAsync) | ✅ (`41222d9`) | bc needs \n to evaluate |
| Phase 4 | Weather response formatting: extract temp/conditions/humidity/wind from snippets | ✅ (`fc32aae`) | Clean one-liner vs raw 5-result dump |

## Next

| Phase | What | Priority |
|-------|------|----------|
| **Deploy** | Build, sync to brazos via shared NFS, restart service, Telegram smoke test. | When ready |

**Design:** `/shared/projects/local-lizard/docs/expanded-intent-router-implementation.md`
**Proposal:** `/shared/projects/local-lizard/docs/expanded-intent-router-proposal.md`
**Goal:** Router handles ~80% of voice queries deterministically. LLM only for remaining 20%.

## Tech Stack (current)

- **Model:** Qwen3 1.7B Q4_K_M via LlamaSharp
- **Format:** GGUF-native chat template (via LLamaTemplate)
- **Tools:** Single-arg designs + deterministic intent router
- **Think blocks:** Stripped server-side
- **Config:** `lizard-config.json` + env var overrides
