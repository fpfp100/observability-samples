# A365 Observability SDK Migration Test Plan

Applies to all languages: .NET, Python, Node.js

Reference: [AO Guide](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=dotnet)

## 1. Scopes — Both Auto and Manual Instrumentation

All 4 scopes must work in both auto and manual modes:

### InvokeAgentScope

- Required attributes: `gen_ai.agent.id`, `gen_ai.agent.name`, `microsoft.a365.agent.blueprint.id`, `microsoft.agent.user.email`, `microsoft.agent.user.id`, `client.address`, `user.id`, `user.email`, `microsoft.channel.name`, `gen_ai.conversation.id`, `gen_ai.input.messages`, `gen_ai.operation.name`, `gen_ai.output.messages`, `server.address`, `server.port`, `microsoft.tenant.id`
- Optional attributes: `error.type`, `gen_ai.agent.description`, `microsoft.a365.agent.platform.id`, `gen_ai.agent.version`, `microsoft.a365.caller.agent.*` (id, name, blueprint.id, platform.id, user.email, user.id, version), `user.name`, `microsoft.channel.link`, `microsoft.conversation.item.link`, `microsoft.session.id`, `microsoft.session.description`
- RecordInputMessages / RecordOutputMessages work correctly

### InferenceScope

- Required attributes: same agent/user/channel/conversation attributes plus `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.input.messages`, `gen_ai.output.messages`
- Optional attributes: `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `microsoft.a365.agent.thought.process`
- Token usage recording: `RecordInputTokens()`, `RecordOutputTokens()`, `RecordFinishReasons()` work correctly

### ExecuteToolScope

- Required attributes: same agent/user/channel attributes plus `gen_ai.tool.call.arguments`, `gen_ai.tool.call.id`, `gen_ai.tool.call.result`, `gen_ai.tool.name`, `gen_ai.tool.type`
- Optional: `gen_ai.tool.description`, `server.address`, `server.port`
- RecordResponse works correctly

### OutputScope

- Required attributes: same agent/user/channel attributes plus `gen_ai.output.messages`
- Async behavior: works correctly when parent scope has already ended
- Parent context linkage: `SpanDetails(parentContext)` correctly links to parent span
- No `server.address`/`server.port` required

## 2. Error Handling on Scopes

- `RecordError()` correctly sets `error.type` attribute and marks span as error
- Scope doesn't crash when error is recorded
- Error spans are still exported

## 3. BaggageBuilder

Attaches context that flows through all spans in a request.

### Core fields
- `tenant_id`
- `agent_id`
- `conversation_id`

### Additional fields
- `caller_agent_id`
- `caller_agent_name`
- `channel`
- `session_id`

### Auto-populate from TurnContext
- `.FromTurnContext(turnContext)` (.NET) / `populate(builder, turn_context)` (Python) / `BaggageBuilderUtils.fromTurnContext()` (JS) correctly extracts caller, agent, tenant, channel, and conversation details from the activity
- All non-empty baggage entries are copied to newly started spans without overwriting existing attributes

## 4. Baggage Middleware

### Middleware Registration (Important Difference)

**Neither the base SDK nor the distro auto-registers BaggageMiddleware or OutputLoggingMiddleware.** Both require explicit registration by the developer:

- **Python (base)**: `ObservabilityHostingManager.configure(adapter.middleware_set, ObservabilityHostingOptions(enable_baggage=True, enable_output_logging=True))` — defaults are `False, False`
- **Python (distro)**: Same API via `microsoft.opentelemetry.a365.hosting.middleware` — also defaults `False, False`
- **.NET (both)**: Manual DI registration of `BaggageTurnMiddleware` and `OutputLoggingMiddleware` as singletons, then added to the adapter middleware pipeline
- **Node.js**: TBD

When testing middleware, ensure the test sample has middleware explicitly enabled. If a sample was written for manual instrumentation only (no middleware), add the registration before testing middleware behavior. Without this, spans will be missing baggage fields and `output_messages` spans will not be emitted.

### BaggageTurnMiddleware
- `BaggageTurnMiddleware` (.NET) / `BaggageMiddleware` (Python/JS) auto-populates baggage from TurnContext for every incoming request
- **ContinueConversation skip**: middleware skips baggage setup for async replies (`ContinueConversation` events) to avoid overwriting baggage from originating request
- HTTP-level baggage middleware (.NET only): `UseObservabilityRequestContext` sets tenant/agent IDs before Bot Framework pipeline runs
- Validate the following baggage fields propagate to all child spans (including HTTP client spans):
  - `gen_ai.agent.id`, `gen_ai.agent.name`, `microsoft.tenant.id`
  - `gen_ai.conversation.id`, `microsoft.channel.name`
  - `microsoft.agent.user.id`, `user.id`, `user.name`
  - `microsoft.conversation.item.link` (serviceUrl)
  - `gen_ai.agent.description`

### OutputLoggingMiddleware
- `OutputLoggingMiddleware` emits `output_messages` spans for async output capture
- Validate `output_messages` spans are emitted from `Agent365Sdk` ActivitySource with Kind=Client
- Validate `output_messages` spans contain:
  - All baggage-derived fields listed above
  - `gen_ai.operation.name`: `output_messages`
  - `gen_ai.output.messages`: structured JSON envelope (`{"messages":[...],"version":"0.1.0"}`)
  - `microsoft.agent.user.email` (should be a real email, not a GUID)
  - `user.email` (should be populated, not empty)
- Validate parent span linkage: `output_messages` should be child of `MessageProcessor` or the background processing span
- Validate both middleware produce consistent results across base SDK and distro

## 5. BatchSpanProcessor

### Console Exporter Flush Timing (Test Infrastructure Note)

When testing with console exporter, spans may not appear in stdout immediately after the request completes. The `SimpleSpanProcessor` exports spans synchronously, but the OTel batch processing pipeline and aiohttp's async event loop introduce delays. **Wait at least 30 seconds after the emulator message completes before capturing console output.** Failing to wait long enough produces misleadingly low span counts (e.g., 0 spans when 40+ actually exist).

This applies to both base SDK (console fallback) and distro (`enable_console=True`). The distro's auto-instrumented spans (e.g., `chat gpt-4o-mini` from `opentelemetry-instrumentation-openai-v2`) are particularly affected because they are emitted after the A365 scope spans.

### Default values set correctly
- `max_queue_size`: 2048
- `max_export_batch_size`: 512
- `scheduled_delay_ms`: 5000
- `exporter_timeout_ms`: 30000 (.NET/Python) / 90000 (JS `exporterTimeoutMilliseconds`) + 30000 (JS `httpRequestTimeoutMilliseconds`)

### Configurable
- All values can be overridden via `Agent365ExporterOptions`
- Custom values are respected at runtime

## 6. Exporter

### `ENABLE_A365_OBSERVABILITY_EXPORTER` environment variable
- `true`: exports to A365 Service endpoint
- `false`: exports to Console (fallback)
- Not set: defaults to Console

### Console exporter
- All scopes and attributes are visible in console output
- Useful for local validation

### A365 exporter
- Correctly partitions spans by tenant/agent identity before export
- Spans missing `tenant_id` or `agent_id` are silently dropped (not crash)
- Log message indicates skipped span count

## 7. TokenResolver

- Returns Bearer token for each export request
- Token is passed to A365 service in authorization header
- Missing or null token resolver: export is skipped (not crash), logged as warning
- Token caching works (`AgenticTokenCache` / `AddAgenticTracingExporter()`)
- Token is resolved per agent_id + tenant_id pair

## 8. Auth

- **OBO (On-Behalf-Of)**: works for user-delegated scenarios
- **S2S (Service-to-Service)**: works for service identity scenarios
- `use_s2s_endpoint` / `UseS2SEndpoint`: when `true`, uses S2S endpoint path; when `false`, uses default OBO path
- HTTP 401 is handled gracefully (logged, not retried, not crash)
- Token audience matches observability endpoint scope (`https://api.powerplatform.com/.default`)

## 9. Auto-Instrumentation per Framework

| Framework | .NET | Python | Node.js |
|-----------|------|--------|---------|
| Semantic Kernel | `WithSemanticKernel()` | `SemanticKernelInstrumentor` | N/A |
| OpenAI | `WithOpenAI()` | `OpenAIAgentsTraceInstrumentor` | `OpenAIAgentsTraceInstrumentor` |
| Agent Framework | `WithAgentFramework()` | `AgentFrameworkInstrumentor` | N/A |
| LangChain | N/A | `CustomLangChainInstrumentor` | `LangChainTraceInstrumentor` |

### Base vs Distro Auto-Instrumentation Behavior (Important Difference)

**Base SDK:** Auto-instrumentation is opt-in. The developer must explicitly call `Instrumentor().instrument()` after `configure()`. Without this call, no framework-level spans (inference, tool) are created — only manually coded scopes produce spans.

**Distro (Python):** `use_microsoft_opentelemetry()` **automatically discovers and instruments all supported libraries** via OTel entry points. The supported list includes: `openai`, `semantic_kernel`, `langchain`, `openai_agents`, `agent_framework`, `requests`, `urllib3`, `django`, `fastapi`, `flask`, `psycopg2`.

This means:
- Distro samples that also call `.instrument()` manually will see "Attempting to instrument while already instrumented" warnings — harmless but redundant.
- Distro openaisample gets OpenAI auto-instrumentation even without an explicit `OpenAIInstrumentor()` call. If the sample also uses manual `InferenceScope`, check for **duplicate inference spans** (one from auto, one from manual).
- When comparing base vs distro, ensure the base sample has `Instrumentor().instrument()` called to match the distro's auto-instrumentation. Without it, the base will have fewer spans.

### Instrumentor Architecture Difference

Instrumentor behavior varies by framework and SDK version:

**Base SDK (v0.3.0.dev6):**
- `SemanticKernelInstrumentor`: **Span processor only** — enriches existing spans but does NOT create inference spans. Base SK samples without manual `InferenceScope` will have no inference spans.
- `CustomLangChainInstrumentor`: **Creates spans** — wraps `BaseCallbackManager.__init__` to attach a tracer that produces `chat` spans (e.g., `chat AzureChatOpenAI`). Requires `wrapt<2` (v2.x renamed `module` parameter to `target`).
- `OpenAIAgentsTraceInstrumentor`: **Creates spans** — bridges the OpenAI Agents SDK's internal tracing to OTel, producing `response`, `turn`, `invoke_agent`, `Agent workflow` spans.

**Distro:** `use_microsoft_opentelemetry()` auto-enables OTel contrib instrumentors (`opentelemetry-instrumentation-openai-v2`, etc.) that create inference spans (`chat gpt-4o-mini`). The A365 span processors enrich those spans with agent/tenant attributes. Both layers work together.

**Version compatibility:** Base SDK extensions v0.1.0 are incompatible with core v0.3.0.dev6. Always use matching versions (all at v0.3.0.dev6).

### Test matrix per framework

For each supported combination:
- **Manual only (no instrumentor):** Verify manual `InferenceScope`/`ExecuteToolScope` produce correct spans with all required attributes
- **Auto only (instrumentor, no manual scopes):** Verify auto-instrumentation captures inference + tool call spans without manual code. **Note:** In the base SDK, this may produce NO inference spans since the instrumentor is a span processor only.
- **Both (instrumentor + manual scopes):** Verify no duplicate spans, or document known duplicates
- **Distro auto vs base manual:** Compare span attributes between distro auto-instrumented inference spans and base manually-scoped inference spans — ensure attribute parity
- Requires BaggageBuilder with agent_id and tenant_id set
- Agent ID in ChatCompletionAgent / agent config must match BaggageBuilder agent_id

## 10. Resource Attributes

- `service.name` is configurable and present (not `unknown_service:...`)
- `service.namespace` is present
- `service.version` is present
- `deployment.environment` is present where applicable
- Values match what was configured at startup

## 11. Configuration Options

- `suppress_invoke_agent_input`: when `true`, suppresses input messages on InvokeAgent spans (Python confirmed; JS available in OpenAI extension; NOT in .NET)
- `ClusterCategory`: prod vs other cluster routing (JS base only via `withClusterCategory()`; NOT in .NET or JS distro)
- `A365_OBSERVABILITY_DOMAIN_OVERRIDE`: custom endpoint for testing (JS/Python only; NOT in .NET)
- `A365_OBSERVABILITY_SCOPES_OVERRIDE`: custom token scope for testing (JS/Python only; NOT in .NET — note: plural "SCOPES" not singular)
- Log level configuration works per platform

## 12. Edge Cases

- **Missing identity**: spans without `tenant_id` or `agent_id` are dropped silently, not exported, count logged
- **Token resolution failure**: graceful skip, warning logged, no crash
- **Exporter timeout**: batch is dropped after timeout, next batch is attempted
- **Large payload**: spans with large input/output messages are handled without truncation errors
- **Multiple concurrent requests**: baggage scopes are isolated per request (no cross-contamination)

## 13. Store Publishing Validation

Per the AO guide, store publishing requires:
- `InvokeAgentScope` implemented with all required attributes
- `InferenceScope` implemented with all required attributes
- `ExecuteToolScope` implemented with all required attributes
- Console exporter output matches the attribute lists in the AO guide
