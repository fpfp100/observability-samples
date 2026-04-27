# Python Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual
- **BA** — Base SDK + Auto
- **DM** — Distro + Manual
- **DA** — Distro + Auto

## Test Progress (as of 2026-04-27)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1 | Scopes (InvokeAgent, Inference, ExecuteTool, Output) | IN PROGRESS | See Phase 1 results below |
| 2 | Error Handling on Scopes | NOT STARTED | |
| 3 | BaggageBuilder | IN PROGRESS | Baggage propagation differs between base and distro |
| 4 | Baggage Middleware | IN PROGRESS | Both registered, but distro invoke_agent missing baggage attrs |
| 5 | BatchSpanProcessor | NOT STARTED | |
| 6 | Exporter | TESTED | Console exporter works both. A365 exporter: 400 TenantIdInvalid (expected local) |
| 7 | TokenResolver | TESTED | Base: fallback only. Distro: AgenticTokenCache works on 2nd call |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - Semantic Kernel | TESTED | Base: no inference span (processor only). Distro: `chat gpt-4o-mini` auto-generated |
| 9b | Auto-instrumentation - OpenAI Agents | TESTED | Base: broken (P-6). Distro: `chat gpt-4o-mini` + `invoke_agent Assistant` auto-generated |
| 9c | Auto-instrumentation - LangChain | BLOCKED | Base: crash P-5. Distro: crash P-7 (KeyError gen_ai.request.model) |
| 10 | Resource Attributes | IN PROGRESS | See attribute comparison below |
| 11 | Configuration Options | NOT STARTED | |
| 12 | Edge Cases | NOT STARTED | |
| 13 | Store Publishing Validation | NOT STARTED | |

## Full Rerun: Console Exporter Span Comparison (All 8 Samples)

### Test Setup
- Console exporter enabled (`ENABLE_A365_OBSERVABILITY_EXPORTER=false` for base, `enable_a365=False, enable_console=True` for distro)
- Message sent: "What is the weather today?" via connector emulator (triggers tool execution in openaisamples)
- 30s flush wait after emulator completes
- All packages at v0.3.0.dev6 (base extensions, core, hosting, runtime)

### Key Span Comparison

| Sample | Total | `invoke_agent` | Inference | Tool | `output_messages` |
|--------|-------|----------------|-----------|------|-------------------|
| **base/openaisample** | 27 | Yes (manual) | `Chat gpt-4o-mini` (manual) | `execute_tool get_weather` (manual) | Yes |
| **distro/openaisample** | 40 | Yes (manual) | **MISSING** (P-2) | **MISSING** (P-2) | Yes |
| base/semantickernelsample | 25 | Yes | None (processor only) | None | Yes |
| distro/semantickernelsample | 40 | Yes | `chat gpt-4o-mini` (intermittent, flush timing) | None | Yes |
| base/langchainsample | 26 | Yes | `chat AzureChatOpenAI` (auto) | None | Yes |
| distro/langchainsample | 41 | Yes | `chat gpt-4o-mini` (auto) | None | Yes |
| base/openaiagentssample | 28 | Yes | `response`, `turn`, `invoke_agent Assistant`, `Agent workflow` (auto) | None | Yes |
| distro/openaiagentssample | 44 | Yes | `chat gpt-4o-mini`, `invoke_agent Assistant` (auto) | None | Yes |

### Attribute Comparison: `invoke_agent` span

| Attribute | Base | Distro | Match? |
|-----------|------|--------|--------|
| `gen_ai.agent.id` | Yes | Yes | OK |
| `gen_ai.agent.name` | Yes | Yes | OK |
| `gen_ai.agent.description` | Yes | Yes | OK |
| `gen_ai.operation.name` | `invoke_agent` | `invoke_agent` | OK |
| `gen_ai.input.messages` | Yes | Yes | OK |
| `gen_ai.conversation.id` | Yes | **Missing** | DIFF |
| `microsoft.tenant.id` | Yes | Yes | OK |
| `microsoft.session.id` | Yes | Yes | OK |
| `microsoft.a365.agent.blueprint.id` | Yes | `microsoft.opentelemetry.a365.agent.blueprint.id` | RENAMED |
| `microsoft.agent.user.email` | Yes | **Missing** | DIFF |
| `microsoft.channel.name` | Yes | **Missing** | DIFF |
| `microsoft.conversation.item.link` | Yes | **Missing** | DIFF |
| `user.id` | Yes | **Missing** | DIFF |
| `user.name` | Yes | **Missing** | DIFF |
| `telemetry.sdk.version` | `0.3.0.dev6` | `0.0.0-unknown` | DIFF |

## Issues Found

### Issue P-1: Distro `invoke_agent` span missing baggage attributes
- **Severity:** HIGH
- **Affects:** All distro samples
- **Details:** The distro's `invoke_agent` span is missing `gen_ai.conversation.id`, `user.id`, `user.name`, `microsoft.agent.user.email`, `microsoft.channel.name`, `microsoft.conversation.item.link`. The base SDK has all of these. Baggage middleware is registered in both, but the distro doesn't propagate baggage into manually-created A365 scopes.

### Issue P-2: Distro missing `Chat gpt-4o-mini` InferenceScope span
- **Severity:** HIGH
- **Affects:** distro/openaisample
- **Details:** Both samples call `InferenceScope.start()` manually with identical patterns, but the distro doesn't emit the span. The base SDK emits it correctly with full attributes (model, tokens, messages, finish reasons).

### Issue P-3: Blueprint ID attribute renamed in distro
- **Severity:** MEDIUM
- **Affects:** All distro samples
- **Details:** Base uses `microsoft.a365.agent.blueprint.id`, distro uses `microsoft.opentelemetry.a365.agent.blueprint.id`. Breaking change for downstream span consumers.

### Issue P-4: Distro SDK version reports `0.0.0-unknown`
- **Severity:** LOW
- **Affects:** All distro samples
- **Details:** `telemetry.sdk.version` is `0.0.0-unknown` in distro vs `0.3.0.dev6` in base.

### Issue P-5: Base `CustomLangChainInstrumentor` requires `wrapt<2`
- **Severity:** MEDIUM (workaround available)
- **Affects:** base/langchainsample
- **Details:** `TypeError: wrap_function_wrapper() got an unexpected keyword argument 'module'` — `wrapt` v2.x renamed the `module` parameter to `target`. Extensions v0.3.0.dev6 still uses the old keyword.
- **Workaround:** Pin `wrapt<2` in pyproject.toml. Applied in sample.
- **File:** `microsoft-agents-a365-observability-extensions-langchain` v0.3.0.dev6

### Issue P-6: Base extensions v0.1.0 incompatible with core v0.3.0.dev6 — RESOLVED
- **Severity:** ~~HIGH~~ RESOLVED
- **Details:** Extensions v0.1.0 was missing constants and had API mismatches with core v0.3.0.dev6. Fixed by upgrading all extensions to v0.3.0.dev6.
- **Resolution:** Pin all extension packages to `==0.3.0.dev6` to match core.

### Issue P-7: Distro LangChain `KeyError: 'gen_ai.request.model'`
- **Severity:** HIGH
- **Affects:** distro/langchainsample
- **Details:** `opentelemetry-instrumentation-openai-v2` crashes in `traced_method` when `gen_ai.request.model` attribute is not set. Happens when LangChain calls Azure OpenAI through its `AzureChatOpenAI` wrapper (model name not in the span attributes dict).
- **File:** `opentelemetry-instrumentation-openai-v2` v2.3b0, `patch.py:118`

### Issue P-8: Distro OpenAI Agents requires OPENAI_API_KEY (not Azure)
- **Severity:** INFO
- **Affects:** distro/openaiagentssample
- **Details:** The `agents` SDK uses OpenAI's API directly, not Azure OpenAI. Needs `OPENAI_API_KEY` env var. Not a bug — just needs different credentials.

### Architecture Finding: Instrumentor behavior varies by framework (v0.3.0.dev6)

**Base SDK:**
- `SemanticKernelInstrumentor`: Span processor only — enriches but does NOT create inference spans
- `CustomLangChainInstrumentor`: **Creates** `chat AzureChatOpenAI` spans (requires `wrapt<2`)
- `OpenAIAgentsTraceInstrumentor`: **Creates** `response`, `turn`, `invoke_agent Assistant`, `Agent workflow` spans

**Distro (`use_microsoft_opentelemetry()`):**
- Auto-enables `opentelemetry-instrumentation-openai-v2` which creates `chat gpt-4o-mini` spans
- SK instrumentor is processor only (same as base); inference comes from openai-v2
- LangChain/OpenAI Agents get spans from both distro instrumentor and openai-v2

**No auto tool spans** — `ExecuteToolScope` requires manual code in all cases.

## Captured Output Files

| Config | File |
|--------|------|
| base/openaisample console spans | `python/base/openaisample/console_spans.log` |
| distro/openaisample console spans | `python/distro/openaisample/console_spans.log` |
