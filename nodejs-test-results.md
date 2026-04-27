# Node.js Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual
- **BA** — Base SDK + Auto
- **DM** — Distro + Manual
- **DA** — Distro + Auto

## Test Progress (as of 2026-04-27)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1 | Scopes (InvokeAgent, Inference, ExecuteTool, Output) | PARTIAL (DA only) | DA: invoke_agent (manual+auto), chat, execute_tool, output_messages all present. See span analysis below. |
| 2 | Error Handling on Scopes | NOT STARTED | |
| 3 | BaggageBuilder | NOT STARTED | |
| 4 | Baggage Middleware | ISSUE FOUND (DA) | Both samples show "Baggage: false" at startup. BaggageMiddleware not enabled in hosting config. |
| 5 | BatchSpanProcessor | TESTED (DA) | Exporter=true: spans routed through BatchSpanProcessor to A365 exporter. No console spans emitted (expected). See exporter analysis. |
| 6 | Exporter | ISSUE FOUND (DA) | Exporter=true: "N spans skipped (missing tenant or agent ID)" — OpenAI: 7 skipped (1+6), LangChain: 9 skipped (1+8). A365 exporter drops auto-instrumented + HTTP spans lacking identity. No export success/failure event logs visible. |
| 7 | TokenResolver | TESTED (DA) | LangChain: token preloaded + cached ("hit"). OpenAI: token cache "miss" — `registerObservability()` not called so cache not pre-populated; custom resolver returns empty string. |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - OpenAI | PASS (DA) | `OpenAIAgentsTraceInstrumentor` active. Spans: invoke_agent, chat, execute_tool all present with gen_ai.* attributes. Warning: "Module @openai/agents loaded before instrumentor" but works. |
| 9b | Auto-instrumentation - LangChain | PASS (DA) | `LangChainTraceInstrumentor` active. Spans: invoke_agent, chat present with gen_ai.* attributes + microsoft.sample_rate. |
| 10 | Resource Attributes | ISSUE FOUND (DA) | service.name/version present. Missing: telemetry.sdk.* attributes (name, language, version). os/host attributes auto-populated. |
| 11 | Configuration Options | TESTED (DA) | exporter=false: console export works. exporter=true: A365 exporter active, console suppressed. Azure Monitor disabled OK. No crash without CONNECTION_STRING. PerRequestExport=false confirmed. |
| 12 | Edge Cases | NOT STARTED | |
| 13 | Store Publishing Validation | NOT STARTED | |

## Closed Bug Validation (distro v0.1.0-alpha.6)

| Issue | Title | Validated | Result |
|-------|-------|-----------|--------|
| #37 | useMicrosoftOpenTelemetry crashes without CONNECTION_STRING | YES | **FIXED** — Both samples start with empty CONNECTION_STRING |
| #39 | Console span export not working when exporters disabled | YES | **FIXED** — Console spans emitted when exporter=false |
| #46 | JsonConfig logs ENOENT for missing applicationinsights.json | YES | **FIXED** — No ENOENT errors in startup logs |
| #53 | Add telemetry.sdk.* attributes | YES | **NOT FIXED** — Resource attributes missing telemetry.sdk.name/language/version |
| #59 | GenAI auto-instrumentations silently ignored | YES | **FIXED** — OpenAI and LangChain auto-instrumented spans present |
| #61 | ObservabilityHostingManager middleware silently skips spans | YES | **FIXED** — output_messages spans present with correct attributes |
| #50 | Exporter event logs missing | YES | **NOT FIXED** — With exporter=true, no export success/failure event logs visible (e.g., `[EVENT]: export-group succeeded`). Only `[export-partition-span-missing-identity] N spans skipped` log appears. No confirmation of export result. |
| #56 | PerRequestSpanProcessor not migrated | YES | **FIXED** (claimed) — `ENABLE_A365_OBSERVABILITY_PER_REQUEST_EXPORT` env var is recognized. With it set to false, BatchSpanProcessor is used. Cannot verify per-request behavior without end-to-end export, but the config path exists. |
| #58 | AgenticTokenCache not migrated | PARTIAL | **PARTIALLY FIXED** — LangChain sample successfully preloads + caches token via `registerObservability()`. OpenAI sample has cache miss (different auth flow). Token resolver callback is invoked by exporter. |
| #40 | PerRequestSpanProcessor config should not be public | YES | **FIXED** — `ENABLE_A365_OBSERVABILITY_PER_REQUEST_EXPORT` is an env var (not a public API option). Config tunables (maxConcurrentExports, maxTraces, maxSpansPerTrace) are env-var-only, matching the fix intent. |
| #42 | Configuration gap no longer supported | YES | **FIXED** — Closed as documentation/design issue. Exporter tunables, serviceNamespace, internal logger injection now handled via env vars or `useMicrosoftOpenTelemetry()` options. |
| #57 | Migration guide issue | YES | **FIXED** — Documentation issue, closed with guide updates. |

## Open Issues Confirmed

| Issue | Title | Confirmed |
|-------|-------|-----------|
| #52 | Auto HTTP span is parent of manual instrumented span | YES — HTTP POST root is parent of manual invoke_agent in both samples |
| #43 | Console exporter returns http span | YES — HTTP spans (GET, POST, outbound) appear in console output |
| #73 | ESM imports don't work | PARTIALLY — LangChain (ESM) works; may affect other import patterns |
| #34 | Filter out non-GenAI spans | YES — HTTP/OAuth spans exported to A365 exporter but skipped due to missing identity |

## Issues Found

### 1. telemetry.sdk.* attributes missing (#53 not fully fixed)
- **Severity:** Medium
- **Affected:** Both OpenAI and LangChain samples
- **Details:** Resource attributes show os.type, os.version, host.name, host.arch, host.id, service.name, service.version but NOT telemetry.sdk.name, telemetry.sdk.language, telemetry.sdk.version. Standard OTel SDK usually auto-adds these.
- **Issue:** #53 was CLOSED but attributes still missing in v0.1.0-alpha.6

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
