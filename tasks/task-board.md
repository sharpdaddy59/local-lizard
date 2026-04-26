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

## Next

| Phase | What | Priority |
|-------|------|----------|
| **Phase 2a — Router cleanup** | Fix false positives in LookupPattern. Cascade router: handlers return null → try next intent → LLM fallthrough. Structured JSON in handler. | High |
| **Phase 2b — New intents** | Weather, math, web search, expanded memory patterns. Deterministic route + cascading fallthrough. | High |
| **Phase 2c — Weather formatting** | Extract temp/conditions from search results. Template response. | Medium |
| **Phase 2d — LLM path tuning** | enable_thinking config option. Test Qwen3.5 4B with thinking disabled. | Low |
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
