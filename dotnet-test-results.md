# .NET Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual (`agent_output.txt`)
- **BA** — Base SDK + Auto (`dotnet/base/semantic-kernel/auto instrument base.txt`)
- **DM** — Distro + Manual (`dotnet/distro/semantic-kernel/agentoutput_manual.txt`)
- **DA** — Distro + Auto (`dotnet/distro/semantic-kernel/autoinstrument_distro.txt`)

## Test Progress (as of 2026-04-26)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1a | Scopes exist | DONE (all 4) | invoke_agent + Chat in all 4. output_messages in BA only. DA missing output_messages. |
| 1b | InvokeAgentScope required attrs | DONE | 5-9 required attrs MISSING across configs. |
| 1c | InferenceScope exists | DONE (all 4) | DM has duplicate Chat span (SK Diagnostics + Agent365Sdk). |
| 1d | InferenceScope required attrs | DONE | gen_ai.provider.name missing in auto mode. 5-6 identity attrs missing. |
| 1e | ExecuteToolScope exists | DONE (BM, DM) | Added DateTimePlugin. Span: `execute_tool DateTimePlugin-get_current_datetime`. |
| 1f | ExecuteToolScope required attrs | DONE (BM, DM) | 10/17 required present. Missing: blueprint.id, user.email, user.id, microsoft.agent.user.email, microsoft.agent.user.id, client.address, gen_ai.tool.type. |
| 1g | OutputScope exists | DONE | BA has 2 output_messages spans. DA does NOT emit. BM/DM: none (by design). |
| 1h | OutputScope required attrs | DONE (BA only) | microsoft.agent.user.email set to GUID (wrong). user.email empty. |
| 2 | Error Handling on Scopes | DONE | Validated from existing outputs. `error.type` set correctly (exception type or HTTP status). `StatusCode: Error` set. Error spans exported. Process doesn't crash. Same identity baggage on error spans. Base auto has richer `Activity.Events` with exception.stacktrace that distro omits. |
| 3a | BaggageBuilder - core fields | DONE | tenant_id/agent_id/conversation_id present as span tags. |
| 3b | BaggageBuilder - additional fields | DONE | caller_agent_id: not set. channel: set. session_id: not set. |
| 3c | BaggageBuilder - TurnContext auto-populate | DONE | Auto mode gets user.id, microsoft.agent.user.id, user.name via middleware. |
| 4a | Baggage Middleware | DONE | Baggage propagation to HTTP spans identical across all 4 auto configs. All fields propagate correctly. |
| 4b | Baggage Middleware - ContinueConversation skip | NOT STARTED | |
| 4c | HTTP-level baggage middleware | NOT STARTED | |
| 4d | OutputLoggingMiddleware | DONE | Distro SK Auto missing all middleware spans (Issue #36). AF: works in both base and distro — output_messages attrs identical. Base AF has HTTP enrichment fields (headers, host, useragent) that distro AF lacks. |
| 5a | BatchSpanProcessor - defaults | DONE | Source code verified: base SDK and distro both match AO guide exactly (max_queue: 2048, batch_size: 512, delay: 5000ms, timeout: 30000ms). Defaults identical between base and distro. |
| 5b | BatchSpanProcessor - configurable | DONE (code review) | Both base and distro pass all 4 values from `Agent365ExporterOptions` to processor constructors. Custom values are accepted. |
| 6a | Exporter - console fallback | DONE | All 4 captured outputs used console exporter. |
| 6b | Exporter - A365 service | DONE | Verified with A365-only export (console disabled). POST to `agent365.svc.cloud.microsoft/observability/tenants/.../otlp/agents/.../traces` returned HTTP 200. Token resolved successfully. Latency: 14.7s. No console span output (confirmed console disabled). No errors or warnings. Output: `dotnet/distro/semantic-kernel/exporter_test_output.txt`. |
| 6c | Exporter - missing identity span drop | DONE | Spans without user identity are NOT dropped — console and A365 exporter both export them. A365 only skips when token resolution fails, not for missing span identity attrs. |
| 7 | TokenResolver | DONE | Default: AgenticTokenCache resolves via OBO, exported to A365 HTTP 200. Custom: `o.Agent365.Exporter.TokenResolver` is called with correct agentId/tenantId (verified). Null return: export silently skipped. Note: custom TokenResolver skips `AddAgenticTracingExporter()` — must manually register `IExporterTokenCache` if agent depends on it. |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - SK | DONE (BA, DA) | Works. DA missing output_messages. DM has duplicate Chat. |
| 9b | Auto-instrumentation - OpenAI | DONE | Sample created at `dotnet/distro/openai/`. OpenAI.ChatClient spans emitted. See below. |
| 9c | Auto-instrumentation - AgentFramework | DONE | Base AF auto: works — invoke_agent, chat, output_messages spans emitted via `A365.AgentFramework` source. Distro AF auto: only output_messages (Issue #34 — custom source name not subscribed). Distro AF manual: works — invoke_agent + Chat from Agent365Sdk, no duplicates. |
| 10a | Resource attrs - base | DONE | Fully compliant. |
| 10b | Resource attrs - distro | DONE - BUG FILED | Issue #28. |
| 11 | Configuration Options | DONE (code review) | `suppress_invoke_agent_input`: NOT in .NET SDK (JS/Python only). `ClusterCategory`: NOT in .NET (JS only). `A365_OBSERVABILITY_DOMAIN_OVERRIDE`/`SCOPES_OVERRIDE`: JS/Python only, not .NET. BatchSpanProcessor defaults configurable via `Agent365ExporterOptions` in both. Log level: generic .NET `Logging.LogLevel` works, no A365-specific category tested. Test plan has errors: these are JS features, not .NET. |
| 12 | Edge Cases | DONE | Missing identity: spans NOT dropped. Token failure: graceful skip, no crash. Large payload: no truncation. Concurrent requests: TESTED — 2 simultaneous msgs with different user/conv contexts, baggage correctly isolated per trace, no cross-contamination. Exporter timeout: 14.8s latency, no timeout enforced. Output: `dotnet/distro/semantic-kernel/concurrent_test_output.txt`. |
| 13 | Store Publishing Validation | DONE | Full checklist run. Multiple gaps found. |

## Issues Found

### By Scope (Base vs Distro, Manual vs Auto)

| # | Issue | Base | Distro | Manual | Auto | Severity |
|---|-------|------|--------|--------|------|----------|
| 1 | `microsoft.a365.agent.blueprint.id` never populated | Yes | Yes | Yes | Yes | Critical |
| 2 | `client.address` never populated | Yes | Yes | Yes | Yes | Critical |
| 3 | `user.email` missing or empty | Yes | Yes | Yes | Yes | Critical |
| 4 | `server.address`/`server.port` missing on invoke_agent | Yes | Yes | Yes | Yes | High |
| 5 | `microsoft.agent.user.email` missing or set to GUID | Yes | Yes | Yes | Yes | High |
| 6 | `microsoft.agent.user.id`/`user.id` missing | Yes | Yes | Manual only | No | High |
| 7 | `gen_ai.provider.name` not set (uses `gen_ai.system`) | Yes | Yes | No | Auto only | Medium |
| 8 | `output_messages` span not emitted | No | Distro only | N/A | Auto only | Medium |
| 9 | Duplicate Chat span (SK Diagnostics + Agent365Sdk) | No | Distro only | Manual only | No | By design — distro enables SK auto-instrumentation by default (`EnableSemanticKernelInstrumentation=true`). Set to `false` to avoid duplicates in manual mode. |
| 10 | Resource attrs: `unknown_service`, missing namespace/version/env | No | Distro only | Yes | Yes | High |
| 11 | `gen_ai.tool.type` missing on ExecuteToolScope | Yes | Yes | Yes | N/A | Medium |
| 12 | Duplicate execute_tool span in distro manual | No | Distro only | Manual only | N/A | By design — same as #9, SK auto-instrumentation enabled by default. |
| 13 | ManualInstrumentationAgent crash after tool use | Yes | Yes | Manual only | N/A | High |
| 14 | `microsoft.agent.user.email` set to GUID on output_messages | Yes | N/A | N/A | Auto only | Medium |

### Filed
- **Issue #28**: Resource attributes missing in distro
- **Issue #29**: Local dev too noisy, InstrumentationOptions too complex
- **Issue #34**: AF auto-instrumentation spans silently dropped when using custom ActivitySource name
- **Issue #35**: SK auto-instrumentation message format not aligned — plain strings instead of structured JSON envelope
- **Issue #36**: Distro SK auto-instrumentation missing output_messages spans from OutputLoggingMiddleware
- **Issue #37**: OpenAI extension helpers not ported to distro (`ChatToolCallExtensions.Trace()`, `OpenAISpanProcessor`)
- **Issue #42**: Custom TokenResolver breaks DI for IExporterTokenCache — migration pitfall from base SDK
- **Issue #45**: SK auto-instrumented spans missing baggage identity attributes — silently dropped by A365 exporter

## ExecuteToolScope Comparison (Base Manual vs Distro Manual)

- Output files: `dotnet/base/semantic-kernel/tool_test_output.txt`, `dotnet/distro/semantic-kernel/tool_test_output.txt`
- Agent365Sdk execute_tool spans are **attribute-identical** between base and distro
- Distro has extra duplicate span from `Microsoft.SemanticKernel` ActivitySource (no agent context attrs)
- Both missing: `gen_ai.tool.type`, `blueprint.id`, user identity attrs
- SDK version: base `0.3.4.11531` vs distro `1.0.0.0`
- ManualInstrumentationAgent crashes on 2nd LLM call after tool use in both (FunctionResultContent not added to chat history)

## AgentFramework Distro Sample

- Location: `dotnet/distro/agent-framework/`
- Status: Builds, starts on port 3978, accepts messages, LLM responds successfully
- Observability: `UseMicrosoftOpenTelemetry()` with `ExportTarget.Agent365`
- User-secrets copied from SK distro sample (same Azure AD app + OpenAI key)
- **Spans emitted**: `output_messages` from `Agent365Sdk` (2 instances) — attributes match SK distro pattern
- **Spans NOT emitted**: `invoke_agent`, `Chat`/inference — `IChatClient` streaming does not produce these without `WithAgentFramework()` auto-instrumentation, and that extension doesn't exist as a separate package in the distro approach
- **Auto mode gap**: The distro `UseMicrosoftOpenTelemetry()` does NOT provide AgentFramework auto-instrumentation equivalent to `WithAgentFramework()` from the base SDK. Only `output_messages` spans come from the middleware.
- **Manual mode works**: `invoke_agent` and `Chat gpt-4o-mini` spans emitted from `Agent365Sdk` with structured JSON message format
- Manual mode attributes match SK distro pattern: same missing identity attrs (blueprint.id, client.address, user.email, etc.)
- No duplicate spans in AgentFramework manual mode (unlike SK distro which has SK Diagnostics duplicates)
- Same resource issues: `service.name` = `unknown_service:AgentFrameworkSampleAgent`, missing namespace/version/env
- IChatClient doesn't auto-invoke tools — LLM returns `{{DateTimePlugin.GetDateTime}}` in text instead of calling it
- Output: `dotnet/distro/agent-framework/agent_output.txt` (auto), `dotnet/distro/agent-framework/manual_output.txt` (manual)

## OpenAI Distro Sample (2026-04-26)

- Location: `dotnet/distro/openai/`
- Uses `OpenAI.Chat.ChatClient` directly (not SK, not Agent Framework) to test `OpenAI.*` activity source
- `AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true)` enables span emission
- `UseMicrosoftOpenTelemetry()` auto-registers `OpenAI.*` source — no explicit `WithOpenAI()` call needed

### Auto mode results

| Span | Source | Status |
|------|--------|--------|
| `chat gpt-4o-mini` | `OpenAI.ChatClient` | YES — `gen_ai.system: openai`, `gen_ai.request.model: gpt-4o-mini`, `gen_ai.operation.name: chat` |
| `output_messages` | `Agent365Sdk` | YES — `gen_ai.output.messages` with structured JSON, all baggage attrs |
| HTTP spans | `System.Net.Http` | YES — POST to Azure OpenAI endpoint, GET/POST to login.microsoftonline.com |
| `POST /api/messages` | `Microsoft.AspNetCore` | YES — root server span |

### Baggage middleware validation

BaggageTurnMiddleware propagates these attributes to all child spans (including `OpenAI.ChatClient`):
- `gen_ai.agent.id`, `gen_ai.agent.name`, `microsoft.tenant.id`
- `gen_ai.conversation.id`, `microsoft.channel.name`
- `microsoft.agent.user.id`, `user.id`, `user.name`
- `microsoft.conversation.item.link` (serviceUrl)

### Span hierarchy (single trace)

```
Microsoft.AspNetCore.Hosting.HttpRequestIn (root)
  └── output_messages (OutputLoggingMiddleware / Agent365Sdk)
        ├── chat gpt-4o-mini (OpenAI.ChatClient — auto-instrumented)
        │     └── POST a365-hw-openai-sentinel.openai.azure.com (System.Net.Http)
        ├── GET login.microsoftonline.com (token resolution)
        └── POST login.microsoftonline.com/oauth2/token (token resolution)
```

### Known gaps (same as other distro samples)
- Resource attrs: `service.name` = `unknown_service:OpenAISampleAgent`, missing namespace/version/env (Issue #28)
- OpenAI call returns 401 due to placeholder API key — telemetry pipeline works regardless
- Tool calling not exercised yet (needs valid API key for LLM to request tool calls)
- Distro missing `ChatToolCallExtensions.Trace()` and `OpenAISpanProcessor` that exist in A365 SDK (Issue #37)
- Output: `dotnet/distro/openai/sample-agent/stdout.txt`

## Captured Output Files

| Config | File |
|--------|------|
| Base Manual | `agent_output.txt` |
| Base Auto | `dotnet/base/semantic-kernel/auto instrument base.txt` |
| Base Manual + Tool | `dotnet/base/semantic-kernel/tool_test_output.txt` |
| Distro Manual | `dotnet/distro/semantic-kernel/agentoutput_manual.txt` |
| Distro Auto | `dotnet/distro/semantic-kernel/autoinstrument_distro.txt` |
| Distro Manual + Tool | `dotnet/distro/semantic-kernel/tool_test_output.txt` |
| AF Distro Auto | `dotnet/distro/agent-framework/agent_output.txt` |
| AF Distro Manual | `dotnet/distro/agent-framework/manual_output.txt` |
| AF Base Auto | `dotnet/base/agent-framework/auto_output.txt` |
| OpenAI Distro Auto | `dotnet/distro/openai/sample-agent/stdout.txt` |
| Concurrent Test (Distro SK Manual) | `dotnet/distro/semantic-kernel/concurrent_test_output.txt` |
| A365 Exporter Test (Distro SK Manual) | `dotnet/distro/semantic-kernel/exporter_test_output.txt` |

## AF Auto: Base vs Distro Comparison (2026-04-25)

**Critical regression**: Distro AF auto loses all core observability spans that base AF auto has.

| Span | AF Base Auto | AF Distro Auto |
|------|-------------|----------------|
| invoke_agent | YES (A365.AgentFramework) | NO |
| chat/inference | YES (A365.AgentFramework) | NO |
| output_messages | YES (Agent365Sdk) | YES (Agent365Sdk) |
| agent.process_message | YES | NO |
| MessageProcessor | YES | NO |

Base AF auto has: structured JSON messages, token usage, system instructions, server.address/port, finish_reasons.
Distro AF auto has: only output_messages from middleware.

Root cause found (2026-04-25): The sample calls `.UseOpenTelemetry(sourceName: "A365.AgentFramework")` which overrides the default ActivitySource name. The distro's `UseAgentFramework()` only subscribes to `Experimental.Microsoft.Agents.AI*` — the custom `A365.AgentFramework` source is silently dropped. The base SDK works because `AgentOTELExtensions.cs` explicitly calls `.AddSource("A365.AgentFramework")`.

Fix options:
1. Remove `sourceName:` override from `.UseOpenTelemetry()` call so it uses default `Experimental.Microsoft.Agents.AI`
2. Or distro should also subscribe to wildcard/custom source names

PR history: Distro PR #11 (April 16) fixed ActivitySource names for M.E.AI 10.4.0, PR #15 (April 17) added AF sources. Both are in v1.0.0-alpha.3 (April 23). The code exists but the source name mismatch causes the gap.

## Concurrent Request Isolation Test (2026-04-26)

Tested with distro SK manual mode (produces duplicate Chat spans for richer trace analysis).
Emulator sent 2 simultaneous messages with different user/conversation contexts:
- User Alpha: `userId=user-A`, `conversationId=conv-AAAA-1111`
- User Beta: `userId=user-B`, `conversationId=conv-BBBB-2222`

**Result: PASS — no cross-contamination**

| Check | Result |
|---|---|
| Distinct traces | YES — TraceId `e644431f...` (User A) and `cc0eab02...` (User B) |
| Conversation isolation | `conv-AAAA-1111` only in trace `e644431f`, `conv-BBBB-2222` only in trace `cc0eab02` |
| Input/output isolation | "hello from user A" → "Hello, User A!" in trace A; "hello from user B" → "Hello, User B!" in trace B |
| Baggage cross-contamination | NONE detected |

Output: `dotnet/distro/semantic-kernel/concurrent_test_output.txt`

## Overall .NET Test Summary (2026-04-26)

### Completion: 27/30 items DONE

| Status | Count | Items |
|--------|-------|-------|
| DONE | 27 | 1a-1h, 2, 3a-3c, 4a, 4d, 5a-5b, 6a-6c, 7, 9a-9c, 10a-10b, 11, 12, 13 |
| NOT STARTED | 3 | 4b (ContinueConversation skip), 4c (HTTP-level baggage middleware), 8 (Auth OBO/S2S) |

### Remaining items

| # | Item | Blocker |
|---|------|---------|
| 4b | BaggageTurnMiddleware ContinueConversation skip | Needs proactive reply code change. Can verify by code review (logic confirmed in SDK source). |
| 4c | HTTP-level baggage middleware (`UseObservabilityRequestContext`) | Optional middleware, not wired in any sample. |
| 8 | Auth (OBO/S2S) | Requires real Azure AD with delegated + app permissions. |

### Bugs Filed (7 total)

| Issue | Title | Repo |
|---|---|---|
| #28 | Resource attributes missing in distro | opentelemetry-distro-dotnet |
| #29 | Local dev too noisy, InstrumentationOptions too complex | opentelemetry-distro-dotnet |
| #34 | AF auto-instrumentation spans silently dropped (custom ActivitySource) | opentelemetry-distro-dotnet |
| #35 | SK auto-instrumentation message format not aligned (plain strings vs JSON) | opentelemetry-distro-dotnet |
| #36 | Distro SK auto missing output_messages from OutputLoggingMiddleware | opentelemetry-distro-dotnet |
| #37 | OpenAI extension helpers not ported to distro | opentelemetry-distro-dotnet |

Resource attributes: Base fully configured (A365.AgentFramework, Microsoft.Agents, 1.0.0, Development). Distro broken (Issue #28).
