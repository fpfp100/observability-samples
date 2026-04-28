# Node.js Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual
- **BA** — Base SDK + Auto
- **DM** — Distro + Manual
- **DA** — Distro + Auto

## Test Progress (as of 2026-04-28, distro v0.1.0-beta.1)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1 | Scopes (InvokeAgent, Inference, ExecuteTool, Output) | PASS (DA) | All 3 samples: invoke_agent (manual+auto), chat, execute_tool, output_messages all present. |
| 2 | Error Handling on Scopes | NOT STARTED | |
| 3 | BaggageBuilder | NOT STARTED | |
| 4 | Baggage Middleware | PARTIAL | basic-agent-sdk: Baggage=true, telemetry.sdk.* propagated on all spans. OpenAI + LangChain: Baggage=false (not configured). |
| 5 | BatchSpanProcessor | TESTED (DA) | Exporter=true: spans routed through BatchSpanProcessor to A365 exporter. No console spans emitted (expected). See exporter analysis. |
| 6 | Exporter | ISSUE FOUND (DA) | Exporter=true: "N spans skipped (missing tenant or agent ID)" — OpenAI: 7 skipped (1+6), LangChain: 9 skipped (1+8). A365 exporter drops auto-instrumented + HTTP spans lacking identity. No export success/failure event logs visible. |
| 7 | TokenResolver | TESTED (DA) | LangChain: token preloaded + cached ("hit"). OpenAI: token cache "miss" — `registerObservability()` not called so cache not pre-populated; custom resolver returns empty string. |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - OpenAI | PASS (DA) | `OpenAIAgentsTraceInstrumentor` active. Spans: invoke_agent, chat, execute_tool all present with gen_ai.* attributes. Warning: "Module @openai/agents loaded before instrumentor" but works. |
| 9b | Auto-instrumentation - LangChain | PASS (DA) | `LangChainTraceInstrumentor` active. Spans: invoke_agent, chat present with gen_ai.* attributes + microsoft.sample_rate. |
| 10 | Resource Attributes | **FIXED in beta.1** | telemetry.sdk.name=A365ObservabilitySDK, telemetry.sdk.language=nodejs, telemetry.sdk.version=0.1.0-beta.1 — present on spans when baggage is enabled (basic-agent-sdk sample). |
| 11 | Configuration Options | TESTED (DA) | exporter=false: console export works. exporter=true: A365 exporter active, console suppressed. Azure Monitor disabled OK. No crash without CONNECTION_STRING. PerRequestExport=false confirmed. |
| 12 | Edge Cases | NOT STARTED | |
| 13 | Store Publishing Validation | NOT STARTED | |

## Bug Validation (distro v0.1.0-beta.1, tested 2026-04-28)

| Issue | Title | Status | Verified | Result |
|-------|-------|--------|----------|--------|
| #37 | useMicrosoftOpenTelemetry crashes without CONNECTION_STRING | CLOSED | PASS | **FIXED** — All 3 samples start with empty CONNECTION_STRING. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/37#issuecomment-4337722690). |
| #39 | Console span export not working when exporters disabled | CLOSED | PASS | **FIXED** — Console spans emitted when exporter=false. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/39#issuecomment-4337723576). |
| #40 | PerRequestSpanProcessor config should not be public | CLOSED | PASS | **FIXED** — Config is env-var only, not public API. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/40#issuecomment-4337724453). |
| #42 | Configuration gap no longer supported | CLOSED | PASS | **FIXED** — Tunables handled via env vars or `useMicrosoftOpenTelemetry()` options. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/42#issuecomment-4337725284). |
| #46 | JsonConfig logs ENOENT for missing applicationinsights.json | CLOSED | PASS | **FIXED** — No ENOENT errors in startup logs. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/46#issuecomment-4337726175). |
| #53 | Add telemetry.sdk.* attributes | CLOSED | PASS | **FIXED in beta.1** — `telemetry.sdk.name=A365ObservabilitySDK`, `telemetry.sdk.language=nodejs`, `telemetry.sdk.version=0.1.0-beta.1` present on spans. Was broken in alpha.6. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/53#issuecomment-4337727036). |
| #56 | PerRequestSpanProcessor not migrated | CLOSED | PASS | **FIXED** — Env var recognized, BatchSpanProcessor used when set to false. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/56#issuecomment-4337727887). |
| #57 | Migration guide issue | CLOSED | PASS | **FIXED** — Documentation closed with guide updates. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/57#issuecomment-4337728722). |
| #59 | GenAI auto-instrumentations silently ignored | CLOSED | PASS | **FIXED** — Both OpenAI and LangChain auto-instrumented spans present. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/59#issuecomment-4337729648). |
| #61 | ObservabilityHostingManager middleware silently skips spans | CLOSED | PASS | **FIXED** — output_messages spans present with correct attributes. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/61#issuecomment-4337730450). |
| #73 | Distro instrumentations don't work with ESM imports | CLOSED | PASS | **FIXED** — LangChain (ESM) works, no ESM errors. [Commented](https://github.com/microsoft/opentelemetry-distro-javascript/issues/73#issuecomment-4337731369). |
| #50 | Exporter event logs missing | CLOSED | FAIL | **NOT FIXED** — No export success/failure event logs visible. Only `[export-partition-span-missing-identity]` appears. |
| #58 | AgenticTokenCache not migrated | CLOSED | PARTIAL | **PARTIAL** — LangChain: token preloaded + cached. OpenAI: cache miss, `registerObservability()` not called. |
| #43 | Console exporter returns http span | CLOSED | FAIL | **NOT FIXED** — HTTP spans still appear in console output (4 in basic, 2 in OpenAI). |
| #34 | Filter out non-GenAI spans | CLOSED | FAIL | **NOT FIXED** — HTTP/OAuth spans still exported, skipped by A365 exporter due to missing identity. |
| #52 | Auto HTTP span is parent of manual instrumented span | **OPEN** | FAIL | **NOT FIXED** — HTTP POST root span is parent of all manual + auto spans. |

## Issues Found

### 1. ~~telemetry.sdk.* attributes missing~~ — FIXED in beta.1
- **Status:** RESOLVED
- **Details:** beta.1 now adds telemetry.sdk.name=A365ObservabilitySDK, telemetry.sdk.language=nodejs, telemetry.sdk.version=0.1.0-beta.1 as span attributes (via baggage propagation). Present on all spans when BaggageMiddleware is enabled.

### 1b. Breaking API change: `isContentRecordingEnabled` removed in beta.1
- **Severity:** Medium (build-breaking)
- **Affected:** All samples using `LangChainInstrumentationConfig` or `OpenAIAgentsInstrumentationConfig`
- **Details:** The `isContentRecordingEnabled` property was removed from both `LangChainInstrumentationConfig` and `OpenAIAgentsInstrumentationConfig` in beta.1. TypeScript compilation fails if set. Samples updated to remove this property.

### 2. Exporter event logs missing (#50 not fully fixed)
- **Severity:** High
- **Affected:** Both samples with exporter=true
- **Details:** No export success/failure event logs like `[EVENT]: export-group succeeded` or `[EVENT]: agent365-export succeeded`. Only the span-skipping partition log appears: `[export-partition-span-missing-identity] N spans skipped`. Partner teams cannot confirm export results.
- **Issue:** #50 was CLOSED but event logs still not visible in v0.1.0-alpha.6

### 3. A365 exporter skips most spans (missing identity)
- **Severity:** High
- **Affected:** Both samples with exporter=true
- **Details:** OpenAI: 7 spans skipped (1 from curl GET + 6 from request). LangChain: 9 spans skipped (1 from curl + 8 from request). The auto-instrumented GenAI spans and HTTP spans lack `microsoft.tenant.id` and `gen_ai.agent.id` attributes, so the A365 exporter drops them. Only the manual Agent365Sdk spans (invoke_agent, output_messages) have identity.
- **Root cause:** Auto-instrumented spans are siblings of the manual scope, not children — they don't inherit baggage/identity from the manual invoke_agent scope.

### 4. OpenAI sample token cache miss
- **Severity:** Medium
- **Affected:** OpenAI sample with exporter=true
- **Details:** `Token cache miss for key: agentic-token-d6d31512-...-badf1f56-...` followed by `tokenResolver called ... token=miss`. The LangChain sample preloads the token successfully, but the OpenAI sample does not call `registerObservability()` to pre-populate the cache.

### 5. Baggage middleware not enabled
- **Severity:** Medium
- **Affected:** Both samples
- **Details:** Startup log shows "Configured. Baggage: false, OutputLogging: true". Need to check if `enableBaggage: true` is being passed to ObservabilityHostingManager.

### 6. OpenAI instrumentor load-order warning
- **Severity:** Low (works despite warning)
- **Affected:** OpenAI sample
- **Details:** "Module @openai/agents has been loaded before microsoft-otel-openai-agents-instrumentor so it might not work, please initialize it before requiring @openai/agents". Auto-instrumentation still works, but warning is concerning for reliability.

### 7. Auto-instrumented spans not nested under manual invoke_agent scope
- **Severity:** Medium
- **Affected:** Both samples
- **Details:** The auto-instrumented GenAI spans and manual Agent365Sdk invoke_agent spans are SIBLINGS under the HTTP root, not nested. This means the A365 exporter cannot partition auto-instrumented spans because they lack identity attributes that would be inherited from the manual scope.

## Span Analysis

### OpenAI Sample (DA config, exporter=false, traceId: a0414fbe...)

```
POST /api/messages [root, @opentelemetry/instrumentation-http]
├── invoke_agent A365 Agent [manual, Agent365Sdk]
│   └── output_messages nikhilcagent0416 Agent User [Agent365Sdk, isRemote: true]
├── execute_tool mcp_MailTools [auto, openai-agent-auto-instrumentation]
├── execute_tool mcp_CalendarTools [auto, openai-agent-auto-instrumentation]
├── invoke_agent OpenAI Agent [auto, openai-agent-auto-instrumentation]
│   └── chat gpt-4.1-2025-04-14 [auto, openai-agent-auto-instrumentation]
└── POST callback to emulator [HTTP auto]
```

### LangChain Sample (DA config, exporter=false, traceId: 52bf9135...)

```
POST /api/messages [root, @opentelemetry/instrumentation-http]
├── invoke_agent LangChainA365Agent [manual, Agent365Sdk]
│   └── output_messages nikhilcagent0416 Agent User [Agent365Sdk, isRemote: true]
├── invoke_agent LangChainA365Agent [auto, microsoft-otel-langchain]
│   └── chat gpt-3.5-turbo [auto, microsoft-otel-langchain]
├── POST token requests (x2) [HTTP auto]
├── POST callback typing (x2) [HTTP auto]
└── POST callback message [HTTP auto, microsoft.sample_rate: 50]
```

### Exporter=true Analysis (both samples)

With exporter enabled:
- **No console spans** — all spans route to A365 exporter (expected)
- **A365 exporter logs:**
  - `[export-partition-span-missing-identity] N spans skipped` — auto-instrumented + HTTP spans dropped
  - Manual `invoke_agent` and `output_messages` spans have identity → should export
  - No `[EVENT]: export-group succeeded/failed` logs (bug #50)
- **OpenAI token:** cache miss → resolver returns empty → export likely fails silently
- **LangChain token:** cache hit → resolver returns valid token → export may succeed (no confirmation log)

## Captured Output Files

| Config | File |
|--------|------|
| DA - OpenAI exporter=false (agent) | `nodejs_openai_distro_output.txt` |
| DA - OpenAI exporter=false (emulator) | `nodejs_openai_emulator_output.txt` |
| DA - LangChain exporter=false (agent) | `nodejs_langchain_distro_output.txt` |
| DA - LangChain exporter=false (emulator) | `nodejs_langchain_emulator_output.txt` |
| DA - OpenAI exporter=true (agent, debug) | `nodejs_openai_exporter_on_output.txt` |
| DA - LangChain exporter=true (agent, debug) | `nodejs_langchain_exporter_on_output.txt` |
| **beta.1 validation** | |
| DA - basic-agent-sdk beta.1 (agent) | `nodejs_beta1_basic_output.txt` |
| DA - OpenAI beta.1 (agent) | `nodejs_beta1_openai_output.txt` |
| DA - LangChain beta.1 (agent) | `nodejs_beta1_langchain_output.txt` |
