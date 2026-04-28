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

**Important:** `InstrumentationMode` is a sample-level config that controls which agent class runs and whether middleware is registered. It does NOT control the distro's auto-instrumentation of framework activity sources.

### Disabling Distro Auto-Instrumentation

The distro's `UseMicrosoftOpenTelemetry()` auto-subscribes to framework activity sources by default. To disable specific auto-instrumentation:

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Disable specific framework auto-instrumentation (all default to true)
        o.Instrumentation.EnableSemanticKernelInstrumentation = false;  // Microsoft.SemanticKernel* sources
        o.Instrumentation.EnableOpenAIInstrumentation = false;          // OpenAI.* sources
        o.Instrumentation.EnableAgentFrameworkInstrumentation = false;  // Experimental.Microsoft.Agents.AI* sources
        o.Instrumentation.EnableAspNetCoreInstrumentation = false;      // ASP.NET Core HTTP
        o.Instrumentation.EnableHttpClientInstrumentation = false;      // Outbound HTTP
        o.Instrumentation.EnableSqlClientInstrumentation = false;       // SQL
        o.Instrumentation.EnableAzureSdkInstrumentation = false;        // Azure SDK
        o.Instrumentation.EnableAgent365Instrumentation = false;        // Agent365 scopes/baggage

        // Master toggles
        o.Instrumentation.EnableTracing = false;   // Disable all tracing
        o.Instrumentation.EnableMetrics = false;    // Disable all metrics
        o.Instrumentation.EnableLogging = false;    // Disable all logging
    });
```

**Note:** Custom activity sources registered via `.WithTracing(t => t.AddSource("MySource"))` are OTel SDK level and are NOT controlled by these distro flags.

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
**Distro gotcha (FIXED in beta.1)**: Previously (alpha.3), setting `TokenResolver` caused the distro to skip registering `IExporterTokenCache<AgenticTokenStruct>`, breaking DI. This is fixed in v1.0.0-beta.1 — custom `TokenResolver` and `IExporterTokenCache` now coexist. Issue #42 closed.

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
- Agent must be running before launching emulator
- Use `AGENT_URL` env var to target specific agent port

### Agent Port Assignments (for concurrent testing)

Each agent has a unique default port (override via `AGENT_PORT` env var):

| Agent | Port | Emulator Command |
|-------|------|-----------------|
| Distro SK | 3978 | `AGENT_URL=http://localhost:3978/api/messages dotnet run` |
| Distro AF | 3979 | `AGENT_URL=http://localhost:3979/api/messages dotnet run` |
| Distro OpenAI | 3980 | `AGENT_URL=http://localhost:3980/api/messages dotnet run` |
| Base SK | 3981 | `AGENT_URL=http://localhost:3981/api/messages dotnet run` |
| Base AF | 3982 | `AGENT_URL=http://localhost:3982/api/messages dotnet run` |

To run all agents concurrently:
```bash
# Start all agents in background
cd dotnet/distro/semantic-kernel/sample-agent && dotnet run &
cd dotnet/distro/agent-framework/sample-agent && dotnet run &
cd dotnet/distro/openai/sample-agent && dotnet run &
cd dotnet/base/semantic-kernel/sample-agent && dotnet run &
cd dotnet/base/agent-framework/sample-agent && dotnet run &

# Send message to each
AGENT_URL=http://localhost:3978/api/messages TESTER_NAME=distro-sk dotnet run
AGENT_URL=http://localhost:3979/api/messages TESTER_NAME=distro-af dotnet run
AGENT_URL=http://localhost:3980/api/messages TESTER_NAME=distro-openai dotnet run
AGENT_URL=http://localhost:3981/api/messages TESTER_NAME=base-sk dotnet run
AGENT_URL=http://localhost:3982/api/messages TESTER_NAME=base-af dotnet run
```

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
