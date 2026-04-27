# Project: A365 Observability SDK to Microsoft.OpenTelemetry Distro Migration

Migration from A365 Observability SDK (`Microsoft.Agents.A365.Observability`) to the Microsoft OpenTelemetry distro (`Microsoft.OpenTelemetry`). Goal: ensure all observability experiences previously supported continue to work after migration.

Reference docs: https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=dotnet

## Migration Test Plan

See [migration-test-plan.md](migration-test-plan.md) for the full cross-language test plan covering scopes, baggage, exporter, auth, auto-instrumentation, and store publishing validation.

## Test Results

Per-language test progress, issues found, and captured output files:
- **.NET:** [dotnet-test-results.md](dotnet-test-results.md)
- **Python:** [python-test-results.md](python-test-results.md)
- **Node.js:** [nodejs-test-results.md](nodejs-test-results.md)

**Important:** After every test run or analysis, update the relevant `*-test-results.md` with the new status and findings.

## .NET

### Project Locations
- **Base (A365 SDK):** `dotnet/base/semantic-kernel/` — uses `Microsoft.Agents.A365.Observability` v0.3.4-beta
- **Distro:** `dotnet/distro/semantic-kernel/` — uses `Microsoft.OpenTelemetry` v1.0.0-alpha.3

### Source Repos
- A365 SDK: https://github.com/microsoft/Agent365-dotnet
- Distro: https://github.com/microsoft/opentelemetry-distro-dotnet/
- Complex sample agents: https://github.com/microsoft/Agent365-Samples/tree/main/dotnet

### Testing Approach
Compare telemetry output across 4 configurations:
1. Base SDK + Manual instrumentation
2. Base SDK + Auto instrumentation
3. Distro + Manual instrumentation
4. Distro + Auto instrumentation

Controlled via `appsettings.json` > `Observability:InstrumentationMode` ("Manual" or "Auto").

### Custom Token Resolver

**Distro** — set on `UseMicrosoftOpenTelemetry` options:
```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
        {
            var token = await GetTokenAsync(agentId, tenantId);
            return token;
        };
    });
```
**Distro gotcha**: When `TokenResolver` is set, the distro skips `AddAgenticTracingExporter()` internally (conditional logic in `UseAgent365()`). If your agent injects `IExporterTokenCache<AgenticTokenStruct>`, you must register it manually. This is different from the base SDK where `AddAgenticTracingExporter()` and `AddA365Tracing()` are always two independent calls — no conditional skip:
```csharp
builder.Services.AddSingleton<IExporterTokenCache<AgenticTokenStruct>, AgenticTokenCache>();
```

**Base SDK** — register `Agent365ExporterOptions` directly:
```csharp
builder.Services.AddSingleton(sp => new Agent365ExporterOptions
{
    TokenResolver = async (agentId, tenantId) =>
    {
        var token = await GetTokenAsync(agentId, tenantId);
        return token;
    }
});
builder.AddA365Tracing();
```

**Default (no custom resolver)** — both base and distro auto-register `AgenticTokenCache` which resolves tokens via OBO using `UserAuthorization` from the `TurnContext`. The cache is populated by calling `RegisterObservability()` in `A365OtelWrapper.cs`.

### Bug Filing
- File issues at: https://github.com/microsoft/opentelemetry-distro-dotnet/issues
- Always draft the issue first and let user review before submitting

### Connector Emulator
- Located at `connector-emulator/`
- Sends messages to `http://localhost:3978/api/messages`
- Agent must be running before launching emulator

## Python

### Project Locations

**Base (A365 SDK):**
- `python/base/openaisample/` — Direct Azure OpenAI, manual scopes
- `python/base/semantickernelsample/` — Semantic Kernel + `SemanticKernelInstrumentor`
- `python/base/langchainsample/` — LangChain + `CustomLangChainInstrumentor`
- `python/base/openaiagentssample/` — OpenAI Agents SDK + `OpenAIAgentsTraceInstrumentor`

**Distro (`microsoft-opentelemetry` v0.1.0a3):**
- `python/distro/openaisample/` — Direct Azure OpenAI, manual scopes
- `python/distro/semantickernelsample/` — Semantic Kernel + `SemanticKernelInstrumentor`
- `python/distro/langchainsample/` — LangChain + `LangChainInstrumentor`
- `python/distro/openaiagentssample/` — OpenAI Agents SDK + `OpenAIAgentsInstrumentor`

### Source Repos
- A365 SDK: https://github.com/microsoft/Agent365-python
- Distro: https://github.com/microsoft/opentelemetry-distro-python
- Complex sample agents: TBD

### Packages
- **Base SDK:** `microsoft-agents-a365-observability-core`, `microsoft-agents-a365-runtime`, plus framework extensions (`microsoft-agents-a365-observability-extensions-semantic-kernel`, `-openai`, `-agent-framework`, `-langchain`)
- **Distro:** `microsoft-opentelemetry`

### Frameworks & Instrumentors
| Framework | Base SDK Instrumentor | Distro Instrumentor |
|-----------|----------------------|---------------------|
| Semantic Kernel | `microsoft_agents_a365.observability.extensions.semantickernel.trace_instrumentor.SemanticKernelInstrumentor` | `microsoft.opentelemetry._semantic_kernel.SemanticKernelInstrumentor` |
| LangChain | `microsoft_agents_a365.observability.extensions.langchain.CustomLangChainInstrumentor` | `microsoft.opentelemetry._genai._langchain.LangChainInstrumentor` |
| OpenAI Agents SDK | `microsoft_agents_a365.observability.extensions.openai.OpenAIAgentsTraceInstrumentor` | `opentelemetry.instrumentation.openai_agents.OpenAIAgentsInstrumentor` |
| Agent Framework | `microsoft_agents_a365.observability.extensions.agentframework.trace_instrumentor.AgentFrameworkInstrumentor` | `microsoft.opentelemetry._agent_framework.AgentFrameworkInstrumentor` |

### Testing Approach
Same 4-config matrix as .NET:
1. Base SDK + Manual instrumentation
2. Base SDK + Auto instrumentation
3. Distro + Manual instrumentation
4. Distro + Auto instrumentation

### Bug Filing
- File issues at: https://github.com/microsoft/opentelemetry-distro-python/issues
- Always draft the issue first and let user review before submitting

### Reference Docs
- AO Guide (Python tab): https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=python

## Node.js

### Project Locations
- **Base (A365 SDK):** `nodejs/base/` — TBD
- **Distro:** `nodejs/distro/` — TBD

### Source Repos
- A365 SDK: https://github.com/microsoft/Agent365-nodejs
- Distro: https://github.com/microsoft/opentelemetry-distro-javascript
- Complex sample agents: TBD

### Packages
- **Base SDK:** `@microsoft/agents-a365-observability`
- **Distro:** `microsoft-opentelemetry`

### Frameworks to Test
- OpenAI (`OpenAIAgentsTraceInstrumentor`)
- LangChain (`LangChainTraceInstrumentor`)

### Testing Approach
Same 4-config matrix as .NET and Python:
1. Base SDK + Manual instrumentation
2. Base SDK + Auto instrumentation
3. Distro + Manual instrumentation
4. Distro + Auto instrumentation

### Bug Filing
- File issues at: https://github.com/microsoft/opentelemetry-distro-javascript/issues
- Always draft the issue first and let user review before submitting

### Reference Docs
- AO Guide (Node.js tab): https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=nodejs
