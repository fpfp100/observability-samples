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
| 5 | BatchSpanProcessor | NOT STARTED | Cannot test with exporter=false |
| 6 | Exporter | NOT STARTED | Cannot test with exporter=false |
| 7 | TokenResolver | PARTIAL (DA) | LangChain sample: token cache working ("Token cached for key"). OpenAI sample: custom resolver wired but exporter disabled. |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - OpenAI | PASS (DA) | `OpenAIAgentsTraceInstrumentor` active. Spans: invoke_agent, chat, execute_tool all present with gen_ai.* attributes. Warning: "Module @openai/agents loaded before instrumentor" but works. |
| 9b | Auto-instrumentation - LangChain | PASS (DA) | `LangChainTraceInstrumentor` active. Spans: invoke_agent, chat present with gen_ai.* attributes + microsoft.sample_rate. |
| 10 | Resource Attributes | ISSUE FOUND (DA) | service.name/version present. Missing: telemetry.sdk.* attributes (name, language, version). os/host attributes auto-populated. |
| 11 | Configuration Options | PARTIAL (DA) | Tested exporter=false (console export works). Azure Monitor disabled OK. No crash without CONNECTION_STRING. |
| 12 | Edge Cases | NOT STARTED | |
| 13 | Store Publishing Validation | NOT STARTED | |

## Closed Bug Validation (distro v0.1.0-alpha.6)

| Issue | Title | Validated | Result |
|-------|-------|-----------|--------|
| #37 | useMicrosoftOpenTelemetry crashes without CONNECTION_STRING | YES | FIXED - Both samples start with empty CONNECTION_STRING |
| #39 | Console span export not working when exporters disabled | YES | FIXED - Console spans emitted in both samples |
| #46 | JsonConfig logs ENOENT for missing applicationinsights.json | YES | FIXED - No ENOENT errors in startup logs |
| #53 | Add telemetry.sdk.* attributes | YES | NOT FIXED - Resource attributes missing telemetry.sdk.name/language/version |
| #59 | GenAI auto-instrumentations silently ignored | YES | FIXED - OpenAI and LangChain auto-instrumented spans present |
| #61 | ObservabilityHostingManager middleware silently skips spans | YES | FIXED - output_messages spans present with correct attributes |
| #50 | Exporter event logs missing | SKIPPED | Cannot test with exporter=false |
| #56 | PerRequestSpanProcessor not migrated | SKIPPED | Cannot test with exporter=false |
| #58 | AgenticTokenCache not migrated | PARTIAL | LangChain shows token caching working; full validation needs exporter=true |
| #40 | PerRequestSpanProcessor config exposure | SKIPPED | Configuration issue, not testable via console |
| #42 | Configuration gap no longer supported | SKIPPED | Documentation issue |
| #57 | Migration guide issue | SKIPPED | Documentation issue |

## Open Issues Confirmed

| Issue | Title | Confirmed |
|-------|-------|-----------|
| #52 | Auto HTTP span is parent of manual instrumented span | YES - HTTP POST root is parent of manual invoke_agent in both samples |
| #43 | Console exporter returns http span | YES - HTTP spans (GET, POST, outbound) appear in console output |
| #73 | ESM imports don't work | PARTIALLY - LangChain (ESM) works; may affect other import patterns |

## Issues Found

### 1. telemetry.sdk.* attributes missing (may be #53 not fully fixed)
- **Severity:** Medium
- **Affected:** Both OpenAI and LangChain samples
- **Details:** Resource attributes show os.type, os.version, host.name, host.arch, host.id, service.name, service.version but NOT telemetry.sdk.name, telemetry.sdk.language, telemetry.sdk.version. Standard OTel SDK usually auto-adds these.
- **Issue:** #53 was CLOSED but attributes still missing in v0.1.0-alpha.6

### 2. Baggage middleware not enabled
- **Severity:** Medium
- **Affected:** Both samples
- **Details:** Startup log shows "Configured. Baggage: false, OutputLogging: true". Need to check if `enableBaggage: true` is being passed to ObservabilityHostingManager.

### 3. OpenAI instrumentor load-order warning
- **Severity:** Low (works despite warning)
- **Affected:** OpenAI sample
- **Details:** "Module @openai/agents has been loaded before microsoft-otel-openai-agents-instrumentor so it might not work, please initialize it before requiring @openai/agents". Auto-instrumentation still works, but warning is concerning for reliability.

### 4. Auto-instrumented spans not nested under manual invoke_agent scope
- **Severity:** Medium
- **Affected:** Both samples
- **Details:** The auto-instrumented GenAI spans (from openai-agent-auto-instrumentation / microsoft-otel-langchain) and manual Agent365Sdk invoke_agent spans are SIBLINGS under the HTTP root, not nested. This means the trace tree doesn't show a clear hierarchy from manual scope -> auto-instrumented GenAI operations.

## Span Analysis

### OpenAI Sample (DA config, traceId: a0414fbe...)

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

**Key attributes observed:**
- `gen_ai.operation.name`: invoke_agent, chat, execute_tool, output_messages
- `gen_ai.provider.name`: openai
- `gen_ai.request.model`: gpt-4.1-2025-04-14
- `gen_ai.usage.input_tokens`: 725, `output_tokens`: 66
- `gen_ai.input.messages` / `gen_ai.output.messages`: content captured
- `gen_ai.tool.name`: mcp_MailTools, mcp_CalendarTools
- `gen_ai.agent.id`, `gen_ai.agent.name`, `microsoft.tenant.id`: present on manual spans

### LangChain Sample (DA config, traceId: 52bf9135...)

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

**Key attributes observed:**
- `gen_ai.operation.name`: invoke_agent, chat, output_messages
- `gen_ai.provider.name`: langchain (auto), openai (auto chat)
- `gen_ai.request.model`: gpt-3.5-turbo
- `gen_ai.usage.input_tokens`: 362, `output_tokens`: 51
- `gen_ai.input.messages` / `gen_ai.output.messages`: content captured
- `microsoft.sample_rate`: 33.33 (chat), 50 (invoke_agent) — sampling attributes present
- `gen_ai.agent.id`, `gen_ai.agent.name`, `microsoft.tenant.id`: present on manual spans

## Captured Output Files

| Config | File |
|--------|------|
| DA - OpenAI (agent) | `nodejs_openai_distro_output.txt` |
| DA - OpenAI (emulator) | `nodejs_openai_emulator_output.txt` |
| DA - LangChain (agent) | `nodejs_langchain_distro_output.txt` |
| DA - LangChain (emulator) | `nodejs_langchain_emulator_output.txt` |
