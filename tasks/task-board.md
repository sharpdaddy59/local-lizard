# LocalLizard Task Board (Revised 2026-04-25)

## Status

The original 12 tasks (T1–T12) are complete. Since then, Metamorph and Flux have been building the **tool calling system** — parser, registry, pipeline, LlmEngine integration — and migrating from `[TOOL_CALL]` to Gemma 4's native `<|tool_call|>` format.

This board replaces the previous version. Tasks are reorganized to reflect what's actually done, what needs testing, and what remains.

## Workflow

Every task follows: **Writer writes → Queues for review → Reviewer approves → Ready to test → Wily signs off**

- Tasks are never assigned to two people — one writes, one reviews.
- Nothing moves to "Done" without review approval.
- The queue is the delivery mechanism. Writer drops artifact in recipient's pending folder.
- If a task is stalled >2h (no progress, no queue response), either agent queues Wily.
- If a reviewer doesn't respond within 2h of receiving a review request, the reviewer gets a nudge.

## Next Actions (current)

| Agent | Action | Waiting On |
|-------|--------|------------|
| **Metamorph** | T15 written and reviewed ✅ | — |
| **Flux** | T15 reviewed and approved ✅ | — |
| **Wily** | T15 signed off — live smoke test passed ✅ | — |
| **Next** | T16 (tool docs) — Flux writes, Metamorph reviews | Flux |

---

## Legend

| Label | Meaning |
|-------|---------|
| **✓ Done** | Code complete, reviewed, passing tests |
| **Content** | Needs work — code, tests, or design |
| **Blocked** | Waiting on something outside this task |

---

## Done (Original 12 Tasks)

| Task | Owner | Notes |
|------|-------|-------|
| T1: C# project skeleton | Metamorph | 2026-04-17 |
| T2: Voice pipeline design | Flux | 2026-04-17 |
| T3: ASP.NET web UI scaffold | Metamorph | 2026-04-18 |
| T4: LLamaSharp model loading | Metamorph | 2026-04-17 |
| T5: Whisper.cpp C# bindings | Flux | 2026-04-17 |
| T6: Piper C# integration | Flux | 2026-04-22 |
| T7: Chat loop | Metamorph | 2026-04-18 |
| T8: Wake word detection | Metamorph | 2026-04-18 |
| T9: Telegram bot interface | Metamorph | 2026-04-18 |
| T10: One-click installer | Metamorph | 2026-04-18 |
| T11: README | Metamorph | 2026-04-19 |
| T12: Hardware compatibility list | Metamorph | 2026-04-19 |

## Done (Tool System — Added Post-Original-12)

| Task | Owner | Code | Unit Tests | Notes |
|------|-------|------|------------|-------|
| **T13: Tool parser** (`ToolCallParser.cs`) | Metamorph | ✓ | 42/xUnit passing | Handles both `[TOOL_CALL]` and `<|tool_call|>` formats |
| **T13b: Tool registry** (`ToolRegistry.cs`) | Metamorph | ✓ | Included in 42 | Registration, lookup, system prompt, case-insensitive |
| **T13c: Tool execution pipeline** (`ToolExecutionPipeline.cs`) | Metamorph | ✓ | Included in 42 | Execution flow, error handling, multi-tool, clean stripping |
| **T13d: Tool implementations** (5 tools) | Metamorph | ✓ | — | get_time, search_web, lookup_fact, remember_fact, run_shell |
| **T14: LlmEngine tool integration** (`CompleteWithToolsAsync`) | Metamorph | ✓ | — | Turn accumulation loop, `InferRawAsync`, `SetToolSystemPrompt` |
| **T9fx: Gemma 4 native format migration** | Metamorph/Flux | ✓ | Tests updated | `<|tool_call|>call:name{}<tool_call|>` tokens, 42/42 passing |

## Done

### T15: End-to-end smoke test (integration)
- **Writer:** Metamorph | **Reviewer:** Flux | **Sign-off:** Wily ✅ *(2026-04-25)*
- Goal: Test tool calls end-to-end.

**All 3 tiers complete:**

**Tier 1 — Unit tests** ✓ *(42 tests passing)*
- Pure C#, no model, no Telegram
- DI-mocked `ITool` implementations
- Covers: ToolCallParser, ToolRegistry, ToolExecutionPipeline, GetTimeTool

**Tier 2 — Integration tests** ✅ *(8/8 passing, reviewed by Flux)*
- Console-mode xUnit test that loads the actual GGUF model (1B Gemma 4)
- Feeds known prompts through `CompleteWithToolsAsync`
- Checks tool call detection + execution + response injection
- **No Telegram API calls** — just model → parser → result

**Tier 3 — Live smoke test** ✅ *(11:14 AM CDT, Apr 25)*
- Bot restarted with native `<|tool>declaration:` system prompt
- All 5 tools verified: `get_time`, `search_web` (Brave API), `remember_fact`, `lookup_fact`, `run_shell`
- `search_web` returned live weather data from Dallas TX
- Config file (`config/lizard-config.json`) replaced env vars for secrets

## Backlog

### T16: Tool documentation
- **Writer:** Flux | **Reviewer:** Metamorph | **Sign-off:** Wily
- Explain tool call format to users
- List available tools, their parameters, and examples
- Document how to add new tools

### T9fx post-migration follow-up
- **Writer:** Flux | **Reviewer:** Metamorph | **Sign-off:** Wily
- Verify Gemma 4 native format handles multi-turn tool conversations
- Confirm EOG token 50 (`<|tool_response>`) stops generation at the right point consistently
- Edge case: tool call at end of context window
