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
| 2 | Error Handling on Scopes | NOT STARTED | Need to trigger error scenario. |
| 3a | BaggageBuilder - core fields | DONE | tenant_id/agent_id/conversation_id present as span tags. |
| 3b | BaggageBuilder - additional fields | DONE | caller_agent_id: not set. channel: set. session_id: not set. |
| 3c | BaggageBuilder - TurnContext auto-populate | DONE | Auto mode gets user.id, microsoft.agent.user.id, user.name via middleware. |
| 4a | Baggage Middleware | DONE | Baggage propagation to HTTP spans identical across all 4 auto configs. All fields propagate correctly. |
| 4b | Baggage Middleware - ContinueConversation skip | NOT STARTED | |
| 4c | HTTP-level baggage middleware | NOT STARTED | |
| 4d | OutputLoggingMiddleware | DONE | Distro SK Auto missing all middleware spans (Issue #36). AF: works in both base and distro — output_messages attrs identical. Base AF has HTTP enrichment fields (headers, host, useragent) that distro AF lacks. |
| 5a | BatchSpanProcessor - defaults | NOT STARTED | |
| 5b | BatchSpanProcessor - configurable | NOT STARTED | |
| 6a | Exporter - console fallback | DONE | All 4 captured outputs used console exporter. |
| 6b | Exporter - A365 service | PARTIAL | DA/DM: POST to agent365.svc.cloud.microsoft HTTP 200. |
| 6c | Exporter - missing identity span drop | NOT STARTED | |
| 7 | TokenResolver | PARTIAL | DA/DM: token resolved, exported to A365 (HTTP 200). |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - SK | DONE (BA, DA) | Works. DA missing output_messages. DM has duplicate Chat. |
| 9b | Auto-instrumentation - OpenAI | DONE | Sample created at `dotnet/distro/openai/`. OpenAI.ChatClient spans emitted. See below. |
| 9c | Auto-instrumentation - AgentFramework | PARTIAL | Sample created. Builds. Needs creds + port fix + observability wiring. |
| 10a | Resource attrs - base | DONE | Fully compliant. |
| 10b | Resource attrs - distro | DONE - BUG FILED | Issue #28. |
| 11 | Configuration Options | NOT STARTED | |
| 12 | Edge Cases | NOT STARTED | |
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
| 9 | Duplicate Chat span (SK Diagnostics + Agent365Sdk) | No | Distro only | Manual only | No | Medium |
| 10 | Resource attrs: `unknown_service`, missing namespace/version/env | No | Distro only | Yes | Yes | High |
| 11 | `gen_ai.tool.type` missing on ExecuteToolScope | Yes | Yes | Yes | N/A | Medium |
| 12 | Duplicate execute_tool span in distro manual | No | Distro only | Manual only | N/A | Medium |
| 13 | ManualInstrumentationAgent crash after tool use | Yes | Yes | Manual only | N/A | High |
| 14 | `microsoft.agent.user.email` set to GUID on output_messages | Yes | N/A | N/A | Auto only | Medium |

### Filed
- **Issue #28**: Resource attributes missing in distro
- **Issue #29**: Local dev too noisy, InstrumentationOptions too complex
- **Issue #34**: AF auto-instrumentation spans silently dropped when using custom ActivitySource name
- **Issue #35**: SK auto-instrumentation message format not aligned — plain strings instead of structured JSON envelope
- **Issue #36**: Distro SK auto-instrumentation missing output_messages spans from OutputLoggingMiddleware
- **Issue #37**: OpenAI extension helpers not ported to distro (`ChatToolCallExtensions.Trace()`, `OpenAISpanProcessor`)

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

Resource attributes: Base fully configured (A365.AgentFramework, Microsoft.Agents, 1.0.0, Development). Distro broken (Issue #28).
