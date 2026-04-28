# .NET Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual (`agent_output.txt`)
- **BA** — Base SDK + Auto (`dotnet/base/semantic-kernel/auto instrument base.txt`)
- **DM** — Distro + Manual (`dotnet/distro/semantic-kernel/agentoutput_manual.txt`)
- **DA** — Distro + Auto (`dotnet/distro/semantic-kernel/autoinstrument_distro.txt`)

## Test Progress (as of 2026-04-27, distro v1.0.0-beta.1)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1a | Scopes exist | DONE | **beta.1 Auto**: invoke_agent + Chat x2 + execute_tool + output_messages — all present. **beta.1 Manual**: invoke_agent + Chat x2 + execute_tool (Agent365Sdk) + 2 SK auto duplicates. |
| 1b | InvokeAgentScope required attrs | DONE | Auto: 9/16 present. Missing: blueprint.id, agent.user.email, client.address, user.email, server.address, server.port. Manual: 8/16 (also missing user.id, microsoft.agent.user.id — no baggage middleware). |
| 1c | InferenceScope exists | DONE | Auto: 2 Chat spans from SK Diagnostics. Manual: 2 Agent365Sdk + 1 SK auto duplicate. |
| 1d | InferenceScope required attrs | DONE | Auto: 9/17. Missing: blueprint.id, agent.user.email, client.address, user.email, gen_ai.provider.name. Manual: 11/17. Has gen_ai.provider.name but missing user identity + server attrs. |
| 1e | ExecuteToolScope exists | DONE | Auto: execute_tool from Microsoft.SemanticKernel (tool auto-invoked!). Manual: Agent365Sdk + SK auto duplicate. |
| 1f | ExecuteToolScope required attrs | DONE | Auto: 9/16. Missing: blueprint.id, agent.user.email, client.address, user.email, tool.call.id, tool.type. Manual: 8/16 + naming bug (`gen_ai.tool.arguments` should be `gen_ai.tool.call.arguments`). |
| 1g | OutputScope exists | DONE | Auto: YES (1 output_messages from Agent365Sdk) — **Issue #36 FIXED in beta.1**. Manual: NO (by design). |
| 1h | OutputScope required attrs | DONE | Auto: 9/12. microsoft.agent.user.email = GUID (wrong). user.email = empty. client.address missing. blueprint.id missing. Structured JSON format correct. |
| 2 | Error Handling on Scopes | DONE | Manual: error.type=System.NotSupportedException, StatusCode=Error on both Chat and invoke_agent. Error spans exported to A365 (HTTP 200). Auto: no errors (clean run). |
| 3a | BaggageBuilder - core fields | DONE | Auto: tenant_id, agent_id, conversation_id on all spans. Manual: only on Agent365Sdk spans (no baggage scope). |
| 3b | BaggageBuilder - additional fields | DONE | caller_agent_id: not set. channel: set. session_id: not set. |
| 3c | BaggageBuilder - TurnContext auto-populate | DONE | Auto: user.id, microsoft.agent.user.id, user.name populated via BaggageTurnMiddleware. Manual: missing (no middleware). |
| 4a | Baggage Middleware | DONE | Auto: all 9 baggage fields on every System.Net.Http span. Manual: no HTTP spans exported (infrastructure instrumentation disabled in A365-only mode). |
| 4b | Baggage Middleware - ContinueConversation skip | NOT STARTED | |
| 4c | HTTP-level baggage middleware | NOT STARTED | |
| 4d | OutputLoggingMiddleware | DONE | Auto: output_messages emitted with structured JSON — **FIXED in beta.1**. Manual: not emitted (by design). |
| 5a | BatchSpanProcessor - defaults | DONE | Verified: max_queue=2048, batch_size=512, delay=5000ms, timeout=30000ms. Same in base and distro. |
| 5b | BatchSpanProcessor - configurable | DONE | Both accept custom values via Agent365ExporterOptions. |
| 6a | Exporter - console fallback | DONE | Both auto and manual output spans to console. |
| 6b | Exporter - A365 service | DONE | Both auto and manual: POST to agent365.svc.cloud.microsoft returned HTTP 200. Latency ~14.7s. |
| 6c | Exporter - missing identity span drop | DONE | Spans without tenant/agent identity silently dropped by PartitionByIdentity. SK auto spans in manual mode dropped (Issue #45). |
| 7 | TokenResolver | DONE | Default AgenticTokenCache works (OBO). Custom TokenResolver verified. Issue #42 **FIXED in beta.1** — custom TokenResolver no longer breaks DI for `IExporterTokenCache<AgenticTokenStruct>`. |
| 8 | Auth (OBO/S2S) | NOT STARTED | Requires real Azure AD. |
| 9a | Auto-instrumentation - SK | DONE | beta.1 Auto: all scopes work, tools auto-invoked, output_messages emitted. beta.1 Manual: Agent365Sdk scopes + SK auto duplicates (duplicates lack identity in manual mode). |
| 9b | Auto-instrumentation - OpenAI | DONE | beta.1: `chat gpt-4o-mini` (OpenAI.ChatClient) + `output_messages` (Agent365Sdk). Same as alpha.3. No infrastructure spans in console (A365-only mode suppresses). |
| 9c | Auto-instrumentation - AgentFramework | DONE - **FIXED** | beta.1 Auto + `.AddSource("A365.AgentFramework")`: all spans present — `agent.process_message`, `MessageProcessor`, `invoke_agent`, `chat gpt-4o-mini`, `output_messages`. Root cause was custom ActivitySource name not registered with OTel SDK. Fix: `.WithTracing(t => t.AddSource("A365.AgentFramework"))`. |
| 10a | Resource attrs - base | DONE | Fully compliant (service.name, namespace, version, environment). |
| 10b | Resource attrs - distro | DONE - **FIXED** | Issue #28. `ConfigureResource()` chained before `UseMicrosoftOpenTelemetry()` works. Verified: `service.name=A365.SemanticKernel`, `service.namespace=Microsoft.Agents`, `service.version=1.0.0`, `deployment.environment=Development`. Distro merges with auto-detected Azure VM attrs. |
| 11 | Configuration Options | DONE | .NET: suppress_invoke_agent_input, ClusterCategory, domain/scope overrides NOT available. JS/Python only. |
| 12 | Edge Cases | DONE | All tested: missing identity (dropped by exporter), token failure (graceful), large payload (no truncation), concurrent requests (isolated), exporter timeout (14.8s, no enforcement). |
| 13a | Disabled auto-instr (manual + middleware) | DONE | Verified via #45 test: manual scopes + SK auto spans all have identity, `output_messages` emitted, all exported HTTP 200. |
| 13b | Disabled middleware (BaggageBuilder active) | DONE | No middleware: manual scopes work, `output_messages` NOT emitted, SK auto spans still have core identity via BaggageBuilder, but missing `user.id`/`user.name`/`microsoft.channel.name`. All exported HTTP 200. |
| 13c | Both middleware + BaggageBuilder disabled | DONE | SK auto span (`Microsoft.SemanticKernel.Diagnostics`) has NO identity — silently dropped by exporter. Manual Agent365Sdk spans have identity (from scope), exported HTTP 200. `output_messages` NOT emitted. |
| 14 | Store Publishing Validation | DONE | All 4 scopes checked. Universally missing: blueprint.id, client.address. user.email always empty. Message format: plain arrays in auto, structured JSON in manual Agent365Sdk spans. |

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
| 10 | Resource attrs: `unknown_service`, missing namespace/version/env | No | **FIXED** — use `ConfigureResource()` before `UseMicrosoftOpenTelemetry()` | Yes | Yes | Was High, now resolved |
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
- **Spans emitted (auto mode, after fix)**: `agent.process_message`, `MessageProcessor`, `invoke_agent`, `chat gpt-4o-mini`, `output_messages` — all from `A365.AgentFramework` or `Agent365Sdk`
- **Issue #34 fix**: Added `.WithTracing(t => t.AddSource("A365.AgentFramework")).WithMetrics(m => m.AddMeter("A365.AgentFramework"))` — custom ActivitySource name must be registered with OTel SDK via standard `.AddSource()` API
- **Manual mode works**: `invoke_agent` and `Chat gpt-4o-mini` spans emitted from `Agent365Sdk` with structured JSON message format
- Manual mode attributes match SK distro pattern: same missing identity attrs (blueprint.id, client.address, user.email, etc.)
- No duplicate spans in AgentFramework manual mode (unlike SK distro which has SK Diagnostics duplicates)
- Resource attrs: **FIXED** — `ConfigureResource()` added, `service.name=A365.AgentFramework`, namespace/version/env all present
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
- Resource attrs: **FIXED** — `ConfigureResource()` added, `service.name=A365.OpenAI`, namespace/version/env all present
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
| **Distro SK Auto (beta.1)** | `dotnet/distro/semantic-kernel/beta1_auto_output.txt` |
| **Distro SK Manual (beta.1)** | `dotnet/distro/semantic-kernel/beta1_manual_output.txt` |

## Distro v1.0.0-beta.1 Retest (2026-04-27)

Upgraded from `Microsoft.OpenTelemetry` v1.0.0-alpha.3 to **v1.0.0-beta.1**.

### SK Auto Mode — Major Fixes

| Span | alpha.3 | beta.1 | Fixed? |
|------|---------|--------|--------|
| `invoke_agent Agent365Agent` | YES | YES | — |
| `Chat gpt-4o-mini` (1st inference) | YES | YES | — |
| `Chat gpt-4o-mini` (2nd, after tool) | NO | **YES** | NEW |
| `execute_tool get_current_datetime` | NO | **YES** | NEW — tool auto-invoked! |
| `output_messages` | NO (Issue #36) | **YES** | **FIXED** |
| Tool invocation | LLM didn't call tool | **Tool invoked + returned time** | FIXED |

### Identity on SK Auto Spans (beta.1)

All SK auto spans now have `microsoft.tenant.id` and `gen_ai.agent.id`:
- `invoke_agent`: YES (from `Microsoft.SemanticKernel.Diagnostics`)
- `Chat gpt-4o-mini`: YES
- `execute_tool get_current_datetime`: YES (from `Microsoft.SemanticKernel`) — has `gen_ai.agent.id`, `microsoft.tenant.id`, `user.id`, `user.name`
- `output_messages`: YES (from `Agent365Sdk`)

### SK Manual Mode (beta.1)

| Span | Source | Identity? |
|------|--------|-----------|
| `invoke_agent Agent365Agent` | Agent365Sdk | YES (manual scope sets it) |
| `Chat gpt-4o-mini` (1st) | Agent365Sdk | YES |
| `execute_tool DateTimePlugin-get_current_datetime` | Agent365Sdk | YES |
| `Chat gpt-4o-mini` (2nd, error) | Agent365Sdk | YES |
| `Chat gpt-4o-mini` (SK auto dup) | Microsoft.SemanticKernel.Diagnostics | **NO** — no baggage scope |
| `execute_tool get_current_datetime` (SK auto dup) | Microsoft.SemanticKernel | **NO** — no baggage scope |

ManualInstrumentationAgent crash **FIXED** — replaced raw `AuthorRole.Tool` chat message with `FunctionResultContent.ToChatMessage()` so SK's OpenAI connector maps tool results back to the correct `tool_call_id`. Both base and distro samples fixed.
SK auto spans in manual mode now have full baggage identity — BaggageBuilder uncommented, middleware always registered (Issue #45 fix).

### Remaining Issues in beta.1

- Issue #28: Resource attributes — **FIXED** by adding `ConfigureResource()` before `UseMicrosoftOpenTelemetry()`. Verified: all 4 attrs present, distro merges with Azure VM auto-detected attrs.
- Issue #35: SK auto-instrumented spans now use structured JSON envelope (`{"messages":[...],"version":"0.1.0"}`) — **FIXED** in beta.1. AF spans still use plain arrays but this matches base AF behavior (comes from `M.E.AI` `.UseOpenTelemetry()` pipeline, not a distro issue).
- Issue #45: SK auto spans in manual mode still lack identity — **NOT FIXED** (sample issue, not distro)
- `microsoft.agent.user.email`: still GUID on output_messages span
- `user.email`: still empty
- ManualInstrumentationAgent crash after tool use: **FIXED** — `FunctionResultContent.ToChatMessage()` replaces raw `AuthorRole.Tool` message

### Issues Fixed in beta.1 (vs alpha.3)

- **Issue #36**: output_messages now emitted in auto mode — **FIXED**
- SK auto now invokes tools correctly in auto mode — **FIXED**
- execute_tool spans now present in auto mode — **FIXED**

### AF Distro Auto (beta.1)

**FIXED** — added `.WithTracing(t => t.AddSource("A365.AgentFramework")).WithMetrics(m => m.AddMeter("A365.AgentFramework"))` after `UseMicrosoftOpenTelemetry()`. All spans now emitted: `agent.process_message`, `MessageProcessor`, `invoke_agent`, `chat gpt-4o-mini`, `output_messages`. The distro subscribes to default MAF sources (`Experimental.Microsoft.Agents.AI*`) but custom source names need explicit `.AddSource()` registration — this is by design per OTel SDK pattern.
Output: `dotnet/distro/agent-framework/beta1_auto_output.txt`

### beta.1 Issue Status Summary

| Issue | Status in beta.1 |
|-------|-----------------|
| #28 Resource attributes | **FIXED** — `ConfigureResource()` before `UseMicrosoftOpenTelemetry()` works. All 3 distro samples updated. |
| #34 AF auto spans dropped | **FIXED** — added `.WithTracing(t => t.AddSource("A365.AgentFramework"))` after `UseMicrosoftOpenTelemetry()`. All spans now emitted. |
| #35 SK auto message format | **FIXED** — SK spans now use structured JSON envelope. AF plain arrays match base (M.E.AI behavior, not distro). |
| #36 SK auto output_messages missing | **FIXED** — output_messages now emitted |
| #42 Custom TokenResolver DI | **FIXED** — full cycle verified: (1) `IExporterTokenCache` resolves in DI with custom TokenResolver set, (2) `RegisterObservability()` writes to cache with no errors, (3) custom TokenResolver called by exporter with correct agentId/tenantId, (4) exporter POSTs to agent365.svc.cloud.microsoft using custom token. |
| #45 SK auto spans no identity (manual) | **FIXED** — middleware now always registered (not gated by auto/manual), BaggageBuilder uncommented. SK auto spans (`Microsoft.SemanticKernel.Diagnostics`, `Microsoft.SemanticKernel`) now have all baggage attrs in manual mode. `output_messages` also emitted. |

Output files: `beta1_auto_output.txt`, `beta1_manual_output.txt`, `dotnet/distro/agent-framework/beta1_auto_output.txt`

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

Resource attributes: Base fully configured. Distro **FIXED** (Issue #28) — all 3 distro samples now use `ConfigureResource()` before `UseMicrosoftOpenTelemetry()`. Verified on SK distro with beta.1: `service.name=A365.SemanticKernel`, `service.namespace=Microsoft.Agents`, `service.version=1.0.0`, `deployment.environment=Development`, merged with Azure VM auto-detected attrs. A365 export HTTP 200.
