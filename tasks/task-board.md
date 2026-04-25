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
| **Wily** | Approve T15 and green-light bot restart for Tier 3 | Metamorph + Flux |

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

## In Progress

### T15: End-to-end smoke test (integration)
- **Writer:** Metamorph | **Reviewer:** Flux | **Sign-off:** Wily
- Goal: Test tool calls end-to-end **without** a Telegram round-trip.

**Tier 1 — Unit tests** ✓ *(42 tests passing)*
- Pure C#, no model, no Telegram
- DI-mocked `ITool` implementations
- Covers: ToolCallParser, ToolRegistry, ToolExecutionPipeline, GetTimeTool

**Tier 2 — Integration tests** ✅ *(8/8 passing, reviewed by Flux)*
- Console-mode xUnit test that loads the actual GGUF model (1B Gemma 4)
- Feeds known prompts through `CompleteWithToolsAsync`
- Checks tool call detection + execution + response injection
- **No Telegram API calls** — just model → parser → result

**Tier 3 — Live smoke test** ⬜ *(blocked, waiting on Wily)*
- Restart the bot on hardware
- Send Telegram messages triggering each tool
- Verify responses in chat

### Design: Integration Test (Tier 2) Strategy
We need to decide the approach before writing code.

#### Options

**Option A: Single-model-load test class**
- Load GGUF model once in `IClassFixture` / constructor
- Feed 5-10 prompts, each triggering different tools
- Assert model produces correct `<|tool_call|>` blocks
- Assert pipeline executes and returns expected results
- ✓ Fast enough for CI (~30s for 10 prompts)
- ✓ No external dependencies
- ⚠️ Needs model file path — should be configurable (appsettings.json or env var)

**Option B: Injected-model test pattern**
- Extract `ILlmEngine` interface
- Inject fake engine for parser/pipeline tests (already done)
- Integration test uses real engine via separate fixture
- ✓ Cleaner separation of concerns
- ✓ Reuses existing mock pattern
- ⚠️ More refactoring needed

**Recommended: Option A** — simpler, faster to implement, gives us real model behavior data immediately. Can refactor to Option B later if the test suite grows.

#### Test cases needed (~8 tests):

| # | Test | What it validates |
|---|------|-------------------|
| 1 | `get_time` with no args | Simplest tool call path |
| 2 | `search_web` with query parameter | Named argument parsing |
| 3 | `remember_fact` with key+value | Multi-arg parsing |
| 4 | Unknown tool name | Dead letter path (fallback to normal response) |
| 5 | Malformed tool block | Parser error recovery |
| 6 | Multiple tool calls in one output | Batch execution |
| 7 | Tool + clean text mixed | Clean output extraction |
| 8 | Model produces no tool call | Passthrough to normal completion |

#### Model prompt templates for tests:
```
"What time is it?"
"Look up the capital of France"
"Remember that my favorite color is blue"
"Run this command: ls -la /tmp"
"Tell me a joke"  → no tool call expected
```

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

## Blockers

1. **Bot restart needed** — Wilywit must restart the bot for any live Tier 3 testing
2. **Tool format decision** — See discussion below: stay native format, switch to simpler format, or accept both
3. **Tier 2 integration tests** — Not written yet, needs format decision first

### Format Decision

Live testing showed the 1B Gemma 4 model doesn't reliably produce `<|tool_call|>` blocks. Options:

| Option | Pros | Cons |
|--------|------|------|
| **A)** Stay on native `<|tool_call|>` | Correct per spec, future-proof | Model often skips it |
| **B)** Switch to `[TOOL_CALL] name:{} arguments:{}` | Model already demonstrated it (test video showed `get_time` working) | Non-standard, custom format |
| **C)** Accept both with fallback parser | Maximum compatibility | More parser complexity |

**Current recommendation:** C — parser already handles both formats. Default system prompt uses native format, but fallback catches `[TOOL_CALL]` blocks if model reverts.
