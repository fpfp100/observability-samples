# Python Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual
- **BA** — Base SDK + Auto
- **DM** — Distro + Manual
- **DA** — Distro + Auto

## Test Progress (as of 2026-04-28, distro upgraded to 0.1.0b1)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1 | Scopes (InvokeAgent, Inference, ExecuteTool, Output) | **DONE** | Base: Inference/Tool/Output all required attrs present. InvokeAgent missing 6. Distro: P-2 missing Inference+Tool |
| 2 | Error Handling on Scopes | **DONE** | Framework error spans work (agents.adapter.process). A365 RecordError() path exists but not triggered in test |
| 3 | BaggageBuilder | **DONE** | Base: ALL baggage fields propagate. Distro: **FIXED** with correct config (`enable_a365=True, EXPORTER=false`). #65 was test config issue — but dual-control is confusing |
| 4 | Baggage Middleware | **DONE** | Both working when `enable_a365=True`. Distro requires `enable_a365=True` for `A365SpanProcessor` to propagate baggage |
| 5 | BatchSpanProcessor | **DONE** | Identical defaults in both (2048/512/5000/30000). Both use `_EnrichingBatchSpanProcessor` subclass |
| 6 | Exporter | **DONE** | Console works both. A365 exporter: identical logic. Both partition by identity, skip on missing |
| 7 | TokenResolver | **DONE** | Identical logic. Base: fallback only at runtime. Distro: AgenticTokenCache works on 2nd call |
| 8 | Auth (OBO/S2S) | **DONE** | Both support `use_s2s_endpoint`. Identical S2S URL construction. Requires real A365 deployment to test |
| 9a | Auto-instrumentation - Semantic Kernel | **DONE** | Base: processor only (no inference span). Distro: `chat gpt-4o-mini` auto-generated |
| 9b | Auto-instrumentation - OpenAI Agents | **DONE** | Base: `response/turn/Agent workflow` spans (v0.3.0.dev6). Distro: `chat gpt-4o-mini` + `invoke_agent Assistant` |
| 9c | Auto-instrumentation - LangChain | **DONE** | Base: `chat AzureChatOpenAI` (v0.3.0.dev6 + wrapt<2). Distro: `chat gpt-4o-mini` (P-7 workaround) |
| 10 | Resource Attributes | **DONE** | Base: service.name/namespace via `configure()`. Distro: **FIXED** — use `resource=Resource.create(...)` param on `use_microsoft_opentelemetry()` |
| 11 | Configuration Options | **DONE** | Code inspection — see details below |
| 12 | Edge Cases | **DONE** | Code inspection — identical handling in both |
| 13 | Store Publishing Validation | **DONE** | Base: passes (with 6 missing invoke_agent attrs). Distro: **FAILS** (P-2, missing Inference+Tool scopes) |

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

### Issue P-1 (#65): Distro baggage not propagating — TEST CONFIG ISSUE + DESIGN FEEDBACK
- **Severity:** ~~CRITICAL~~ DESIGN FEEDBACK
- **Status:** Baggage works when `enable_a365=True`. Was caused by using `enable_a365=False` which disables `A365SpanProcessor`. Filed feedback that `enable_a365` controls both exporter AND baggage — confusing dual-control with `ENABLE_A365_OBSERVABILITY_EXPORTER`.
- **Correct config:** `enable_a365=True` + `ENABLE_A365_OBSERVABILITY_EXPORTER=false` + `enable_console=True`

### Issue P-2 (#66): Distro manual InferenceScope/ExecuteToolScope spans not emitted
- **Severity:** HIGH — **STILL OPEN in 0.1.0b1**
- **Affects:** distro/openaisample
- **Details:** Spans are created (confirmed in server log) but do not reach any exporter. `invoke_agent` and `output_messages` reach exporters, but `Chat` and `execute_tool` do not. Same code pattern works in base SDK.

### Issue P-3 (#68): Blueprint ID attribute renamed
- **Severity:** ~~MEDIUM~~ **FIXED in 0.1.0b1**
- **Details:** Now uses `microsoft.a365.agent.blueprint.id` (matching base SDK).

### Issue P-4 (#69): Distro SDK version `0.0.0-unknown`
- **Severity:** ~~LOW~~ **FIXED in 0.1.0b1**
- **Details:** Now reports `telemetry.sdk.version: 0.1.0b1`.

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

### Issue P-7 (#70): Distro LangChain `KeyError: 'gen_ai.request.model'`
- **Severity:** ~~HIGH~~ **FIXED in 0.1.0b1**
- **Details:** `opentelemetry-instrumentation-openai-v2` no longer crashes when `model=None`. `chat gpt-4o-mini` span now appears correctly.

### Issue P-9 (#67): Distro resource attributes missing service.name/namespace
- **Severity:** ~~HIGH~~ **FIXED in 0.1.0b1**
- **Details:** Use `resource=Resource.create({"service.name": "...", "service.namespace": "..."})` parameter on `use_microsoft_opentelemetry()`. Replaces base SDK's `configure(service_name=..., service_namespace=...)`.

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

### Issue P-9: Distro resource attributes missing service.name/namespace
- **Severity:** HIGH
- **Affects:** All distro samples
- **Details:** Distro resource shows `service.name: unknown_service` and missing `service.namespace`. Base SDK correctly sets both from `configure(service_name=..., service_namespace=...)`. The distro's `use_microsoft_opentelemetry()` doesn't propagate these to the OTel resource.

### Issue P-1 (UPGRADED): Distro baggage not propagating to ANY child spans
- **Severity:** CRITICAL
- **Affects:** All distro samples
- **Original:** Distro `invoke_agent` span missing baggage attributes
- **Updated finding:** ALL 10 baggage fields are missing from ALL framework spans in the distro (`agents.app.*`, `agents.turn.*`, `agents.connector.*`, `agents.adapter.send_activities`). Only `output_messages` has partial baggage (missing `microsoft.conversation.item.link`). The base SDK propagates all 10 fields to all spans. The distro's BaggageMiddleware is registered but appears non-functional.

## Attribute Completeness (Test 1 + Test 13)

### Base SDK `invoke_agent` — 6 required attributes missing

| Missing Attribute | Notes |
|-------------------|-------|
| `microsoft.agent.user.id` | Not set; `user.id` is set from baggage |
| `client.address` | Not populated from emulator activity |
| `user.email` | Not set; `microsoft.agent.user.email` is set (different key) |
| `gen_ai.output.messages` | Not recorded on invoke_agent; handled by output_messages scope |
| `server.address` | Not set by sample (developer must configure) |
| `server.port` | Not set by sample (developer must configure) |

### Base SDK InferenceScope, ExecuteToolScope, OutputScope
All required attributes present.

### Resource Attributes

| Attribute | Base | Distro | Required |
|-----------|------|--------|----------|
| `service.name` | `AzureOpenAiKairoTracing` | **`unknown_service`** (P-9) | Yes |
| `service.namespace` | `AzureOpenAiKairoTesting` | **Missing** (P-9) | Yes |
| `service.version` | Missing | Missing | Yes |
| `telemetry.sdk.language` | `python` | `python` | — |
| `telemetry.sdk.name` | `opentelemetry` | `opentelemetry` | — |
| `telemetry.sdk.version` | `1.38.0` | `1.40.0` | — |

### Baggage Propagation

| Span Category | Base (10 fields) | Distro (10 fields) |
|---------------|-----------------|-------------------|
| `agents.app.*` | ALL present | **ALL 10 missing** |
| `agents.turn.*` | ALL present | **ALL 10 missing** |
| `agents.connector.*` | ALL present | **ALL 10 missing** |
| `agents.adapter.send_activities` | ALL present | **ALL 10 missing** |
| `invoke_agent` (A365) | ALL present | **6 missing** |
| `output_messages` (A365) | ALL present | 1 missing (`conversation.item.link`) |
| `Chat / execute_tool` (A365) | ALL present | **Not emitted** (P-2) |

### Store Publishing Readiness

| Requirement | Base | Distro |
|-------------|------|--------|
| InvokeAgentScope with required attrs | Partial (6 missing) | Partial (6+ missing, no baggage) |
| InferenceScope with required attrs | **PASS** | **FAIL** (P-2: not emitted) |
| ExecuteToolScope with required attrs | **PASS** | **FAIL** (P-2: not emitted) |
| Console output matches AO guide | Mostly | **FAIL** |

## Code Inspection Results (Tests 5, 8, 11, 12)

### Test 5: BatchSpanProcessor

| Setting | Base Default | Distro Default | Test Plan | Match? |
|---------|-------------|----------------|-----------|--------|
| `max_queue_size` | 2048 | 2048 | 2048 | OK |
| `max_export_batch_size` | 512 | 512 | 512 | OK |
| `scheduled_delay_ms` | 5000 | 5000 | 5000 | OK |
| `exporter_timeout_ms` | 30000 | 30000 | 30000 | OK |

Both use `_EnrichingBatchSpanProcessor(BatchSpanProcessor)` subclass that enriches spans before batching. Configurable via `Agent365ExporterOptions` / `SpectraExporterOptions`. **Identical between base and distro.**

### Test 8: Auth (OBO/S2S)

| Feature | Base | Distro | Match? |
|---------|------|--------|--------|
| `use_s2s_endpoint` parameter | Yes (`Agent365ExporterOptions`) | Yes (`Agent365ExporterOptions`) | OK |
| S2S URL path | `/observabilityService/tenants/{t}/otlp/agents/{a}/traces?api-version=1` | Same | OK |
| Default (OBO) URL path | Standard OBO endpoint | Same | OK |
| HTTP 401 handling | `_post_with_retries` handles non-retryable errors | Same | OK |

**Identical S2S/OBO logic.** Requires real A365 deployment to test runtime behavior.

### Test 11: Configuration Options

| Option | Base | Distro | Match? |
|--------|------|--------|--------|
| `suppress_invoke_agent_input` | `configure(suppress_invoke_agent_input=True)` | `_EnrichingBatchSpanProcessor(suppress_invoke_agent_input=True)` + env `A365_SUPPRESS_INVOKE_AGENT_INPUT` | **Distro adds env var support** |
| `A365_OBSERVABILITY_DOMAIN_OVERRIDE` | Supported via `get_validated_domain_override()` | Supported, constant in `a365/constants.py` | OK |
| `A365_OBSERVABILITY_SCOPE_OVERRIDE` | **Not found** in base core | Supported in distro `runtime/environment_utils.py` | **Distro only** |
| `use_s2s_endpoint` | Supported | Supported | OK |
| `cluster_category` | Supported in `_Agent365Exporter` constructor | Supported | OK |

Differences:
- Distro adds **env var** `A365_SUPPRESS_INVOKE_AGENT_INPUT` (base requires code parameter)
- Distro has `A365_OBSERVABILITY_SCOPE_OVERRIDE` for custom token scopes (base does not)

### Test 12: Edge Cases

| Edge Case | Base | Distro | Match? |
|-----------|------|--------|--------|
| Missing identity (no tenant/agent) | `partition_by_identity` returns empty → "No spans with identity found" → `SUCCESS` | Identical | OK |
| Token resolution failure | Catches exception, logs error, marks group as failure, continues to next group | Identical | OK |
| Token returns None | Logs debug "No token returned", proceeds without auth header | Identical | OK |
| Non-HTTPS with token | Warns "Bearer token sent over non-HTTPS" | Identical | OK |
| Exporter closed | Returns `FAILURE` immediately | Identical | OK |
| Export HTTP error | `_post_with_retries` handles retries and non-retryable errors | Identical logic | OK |

**All edge case handling is identical between base and distro.** The A365 exporter code (`_Agent365Exporter`) is the same codebase ported to the distro namespace.

## Captured Output Files

| Config | File |
|--------|------|
| base/openaisample console spans | `python/base/openaisample/console_spans.log` |
| distro/openaisample console spans | `python/distro/openaisample/console_spans.log` |
