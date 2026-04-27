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

## Phase 1: Console Exporter Span Comparison (OpenAI + SK)

### Test Setup
- Console exporter enabled (A365 exporter disabled)
- Message sent: "What is 2+2?" via connector emulator
- Both base and distro use same credentials and agent identity

### OpenAI Sample: Span Comparison

| Span | Base (manual) | Distro (manual) |
|------|--------------|-----------------|
| `invoke_agent` | Yes, all attrs | Yes, **missing baggage attrs** (see Issue #1) |
| `Chat gpt-4o-mini` (InferenceScope) | Yes, full attrs | **MISSING** (see Issue #2) |
| `output_messages` | Yes | Yes |
| HTTP client spans | No | Yes (GET/POST from requests instrumentor) |
| Metrics | No | Yes (turn.count, duration, etc.) |

### Semantic Kernel Sample: Span Comparison

| Span | Base (auto) | Distro (auto) |
|------|------------|---------------|
| `invoke_agent` | Yes, all attrs | Yes, **missing baggage attrs** |
| Inference span | **No** (instrumentor is span-processor only) | **No** (same — SK instrumentor doesn't create spans) |
| `output_messages` | Yes | Yes |

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

### Issue P-5: Base `CustomLangChainInstrumentor` crash on init
- **Severity:** HIGH
- **Affects:** base/langchainsample
- **Details:** `TypeError: wrap_function_wrapper() got an unexpected keyword argument 'module'` — the base LangChain extension is incompatible with the installed `opentelemetry-instrumentation` version.
- **File:** `microsoft-agents-a365-observability-extensions-langchain` v0.1.0

### Issue P-6: Base `OpenAIAgentsTraceInstrumentor` import crash
- **Severity:** HIGH
- **Affects:** base/openaiagentssample
- **Details:** `ImportError: cannot import name 'GEN_AI_SYSTEM_KEY' from 'microsoft_agents_a365.observability.core.constants'` — the OpenAI extensions package references a constant that doesn't exist in `observability-core` v0.3.0.dev6.
- **File:** `microsoft-agents-a365-observability-extensions-openai` v0.1.0

### Issue P-7: Distro LangChain `KeyError: 'gen_ai.request.model'`
- **Severity:** HIGH
- **Affects:** distro/langchainsample
- **Details:** `opentelemetry-instrumentation-openai-v2` crashes in `traced_method` when `gen_ai.request.model` attribute is not set. Happens when LangChain calls Azure OpenAI through its `AzureChatOpenAI` wrapper (model name not in the span attributes dict).
- **File:** `opentelemetry-instrumentation-openai-v2` v2.3b0, `patch.py:118`

### Issue P-8: Distro OpenAI Agents requires OPENAI_API_KEY (not Azure)
- **Severity:** INFO
- **Affects:** distro/openaiagentssample
- **Details:** The `agents` SDK uses OpenAI's API directly, not Azure OpenAI. Needs `OPENAI_API_KEY` env var. Not a bug — just needs different credentials.

### Architecture Finding: Base instrumentors are span processors, not span creators
- Base SDK `SemanticKernelInstrumentor`, `CustomLangChainInstrumentor`, `OpenAIAgentsTraceInstrumentor` add SpanProcessors that enrich existing spans — they do NOT create inference spans.
- Distro `use_microsoft_opentelemetry()` auto-enables OTel contrib instrumentors (`opentelemetry-instrumentation-openai-v2`, etc.) that DO create inference spans.
- Base framework samples without manual `InferenceScope` will have NO inference spans.

## Phase 2: Auto-Instrumentation Results (All Frameworks)

Tested with console exporter, `ENABLE_A365_OBSERVABILITY_EXPORTER=false`, 30s flush wait.

### Base SDK (auto-instrumentation only, no manual InferenceScope/ExecuteToolScope)

| Sample | Reply | Total Spans | Inference Span | Tool Span |
|--------|-------|-------------|----------------|-----------|
| base/semantickernelsample | "2 + 2 equals 4." | 25 | **No** (processor only) | No |
| base/langchainsample | "2 + 2 equals 4." | 25 | **No** (processor only) | No |
| base/openaiagentssample | "2+2 equals 4." | 25 | **No** (processor only) | No |

All base framework samples produce `invoke_agent` + `output_messages` A365 spans but **no inference or tool spans**. The base SDK instrumentors are span processors — they enrich but don't create.

### Distro (auto-instrumentation via `use_microsoft_opentelemetry()`)

| Sample | Reply | Total Spans | Inference Span | Tool Span |
|--------|-------|-------------|----------------|-----------|
| distro/semantickernelsample | "2 + 2 equals 4." | 71 | **Yes**: `chat gpt-4o-mini` | No |
| distro/langchainsample | Error (P-7) | 40 | **No** (crash before inference) | No |
| distro/openaiagentssample | "2 + 2 = 4." | 44 | **Yes**: `chat gpt-4o-mini-2024-07-18` + `invoke_agent Assistant` | No |

Key findings:
- **distro/semantickernelsample**: The `opentelemetry-instrumentation-openai-v2` auto-instrumentor successfully creates `chat gpt-4o-mini` inference spans for SK's underlying OpenAI client calls. Requires 30s flush wait — SimpleSpanProcessor is immediate but batched OTel processing introduces delay.
- **distro/openaiagentssample**: Produces both a `chat` inference span from `openai-v2` instrumentor AND an `invoke_agent Assistant` span from the OpenAI Agents SDK's own tracing bridge.
- **distro/langchainsample**: Still crashes with P-7 (`KeyError: 'gen_ai.request.model'`) — the `openai-v2` instrumentor fails when LangChain's `AzureChatOpenAI` wrapper calls the API without setting the model attribute in span attributes.
- **No tool spans** in any framework sample — auto-instrumentation does not create `ExecuteToolScope` spans. Tool spans require manual `ExecuteToolScope` code.

## Captured Output Files

| Config | File |
|--------|------|
| base/openaisample console spans | `python/base/openaisample/console_spans.log` |
| distro/openaisample console spans | `python/distro/openaisample/console_spans.log` |
