# Project: A365 Observability SDK to Microsoft.OpenTelemetry Distro Migration

Migration from A365 Observability SDK (`Microsoft.Agents.A365.Observability`) to the Microsoft OpenTelemetry distro (`Microsoft.OpenTelemetry`). Goal: ensure all observability experiences previously supported continue to work after migration.

Reference docs: https://learn.microsoft.com/en-us/microsoft-agent-365/developer/observability?tabs=dotnet

## Migration Test Plan

See [migration-test-plan.md](migration-test-plan.md) for the full cross-language test plan covering scopes, baggage, exporter, auth, auto-instrumentation, and store publishing validation.

## Test Results

See [dotnet-test-results.md](dotnet-test-results.md) for .NET test progress, issues found, and captured output files.

**Important:** After every test run or analysis, update `dotnet-test-results.md` with the new status and findings.

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

### Bug Filing
- File issues at: https://github.com/microsoft/opentelemetry-distro-dotnet/issues
- Always draft the issue first and let user review before submitting

### Connector Emulator
- Located at `connector-emulator/`
- Sends messages to `http://localhost:3978/api/messages`
- Agent must be running before launching emulator

## Python

### Project Locations
- **Base (A365 SDK):** `PythonSampleAgent/` — uses `microsoft-agents-a365-observability-core` + `microsoft-agents-a365-runtime`
- **Distro:** TBD — will use `microsoft-opentelemetry`

### Source Repos
- A365 SDK: https://github.com/microsoft/Agent365-python
- Distro: https://github.com/microsoft/opentelemetry-distro-python
- Complex sample agents: TBD

### Packages
- **Base SDK:** `microsoft-agents-a365-observability-core`, `microsoft-agents-a365-runtime`, plus framework extensions (`microsoft-agents-a365-observability-extensions-semantic-kernel`, `-openai`, `-agent-framework`, `-langchain`)
- **Distro:** `microsoft-opentelemetry`

### Frameworks to Test
- Semantic Kernel (`SemanticKernelInstrumentor`)
- LangChain (`CustomLangChainInstrumentor`)
- OpenAI Agents SDK (`OpenAIAgentsTraceInstrumentor`)
- Microsoft Agent Framework (`AgentFrameworkInstrumentor`)

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
