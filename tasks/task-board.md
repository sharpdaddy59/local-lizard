# LocalLizard — Current Status

## Where We Are

All 15 original tasks (T1–T15) complete. Shifting to the **Headless Voice Agent** architecture.

## Migrated

| Phase | What | Status | Notes |
|-------|------|--------|-------|
| Phase 0 | Qwen 2.5 3B Q4_K_M downloaded + config swap | ✅ (Apr 26) | 1.8GB, 5-8 tok/s on brazos |
| Phase 1a | GBNF lazy grammar + ChatML prompt format | ✅ (`afbd2f7`) | Grammatically-correct tool calls, no retries |
| Phase 1b | Deterministic intent router (5 intents) | ✅ (`afbd2f7`) | get_time: 0.0ms vs 9.9s; unmatched fall through to LLM |

## Next

| Step | Who | What |
|------|-----|------|
| **Phase 2 eval** | Flux + Metamorph | Evaluate if Agent Framework migration is worth it. GBNF + router may cover the same reliability gains without framework risk. |
| **Build + test** | Flux | Compile and run integration tests against Qwen 2.5 3B on aransas |
| **Voice pipeline** | Future | Phase 3 when ready: state machine, VAD, barge-in |

## Tech Stack (current)

- **Model:** Qwen 2.5 3B Q4_K_M via LlamaSharp
- **Format:** ChatML (`<|im_start|>`)
- **Tools:** Grammar-constrained (GBNF) + deterministic intent router
- **Config:** `lizard-config.json` + env var overrides
