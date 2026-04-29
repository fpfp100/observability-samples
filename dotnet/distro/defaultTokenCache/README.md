# Default Token Cache Sample (.NET Distro)

Demonstrates using the distro's **built-in `AgenticTokenCache`** for A365 observability export — no custom token resolver, no custom token cache class.

## How It Works

The `Microsoft.OpenTelemetry` distro auto-registers `AgenticTokenCache` when no custom `TokenResolver` is set:

```
UseMicrosoftOpenTelemetry()
  -> UseAgent365()
    -> options.Exporter.TokenResolver == null?
      -> YES -> AddAgenticTracingExporter() -> registers IExporterTokenCache<AgenticTokenStruct>
    -> AddAgent365Exporter() -> exporter calls cache at export time
```

The developer only needs to **populate the cache** by calling `RegisterObservability()` during message handling.

## Setup

### 1. Program.cs — Do NOT set TokenResolver

```csharp
builder.Services.AddOpenTelemetry()
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;
        if (builder.Environment.IsDevelopment())
            o.Exporters |= ExportTarget.Console;
        // NO o.Agent365.Exporter.TokenResolver — let the distro auto-register AgenticTokenCache
    });
```

### 2. Agent — Inject `IExporterTokenCache` and call `RegisterObservability()`

```csharp
public class MinimalAgent : AgentApplication
{
    private readonly IExporterTokenCache<AgenticTokenStruct> _tokenCache;

    public MinimalAgent(
        AgentApplicationOptions options,
        IExporterTokenCache<AgenticTokenStruct> tokenCache, ...) : base(options)
    {
        _tokenCache = tokenCache;
    }

    protected async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
    {
        // Resolve agent and tenant identity
        var agentId = turnContext.Activity.IsAgenticRequest()
            ? turnContext.Activity.GetAgenticInstanceId()
            : Guid.NewGuid().ToString();
        var tenantId = turnContext.Activity.Conversation?.TenantId ?? Guid.Empty.ToString();

        // Populate the built-in token cache (one call per message)
        try
        {
            _tokenCache.RegisterObservability(
                agentId,
                tenantId,
                new AgenticTokenStruct(
                    userAuthorization: UserAuthorization,
                    turnContext: turnContext,
                    authHandlerName: "agentic"),
                EnvironmentUtils.GetObservabilityAuthenticationScope());
        }
        catch (Exception ex)
        {
            // Non-fatal — export will skip if no token available
            logger?.LogWarning($"Token cache registration failed: {ex.Message}");
        }

        // ... your agent logic (SK, OpenAI, etc.)
    }
}
```

### 3. That's it

The A365 exporter automatically calls `AgenticTokenCache.GetToken(agentId, tenantId)` at export time. The cache handles:
- Per agent+tenant pair isolation
- Token expiry (based on JWT `exp` claim)
- Reuse across multiple messages (no duplicate OBO exchanges)

## Token Cache Behavior

| Scenario | What happens |
|----------|-------------|
| First message | Cache miss -> OBO token exchange via MSAL -> token cached |
| Subsequent messages (same pair) | Cache hit -> reuses cached token, no exchange |
| Token expired | Cache detects expiry -> re-exchanges on next `RegisterObservability()` |
| Export time | Exporter calls `GetToken()` -> returns cached token -> HTTP POST to A365 |
| No token available | Export silently skipped, warning logged |

## Running

```bash
cd dotnet/distro/defaultTokenCache/sample-agent
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

Default port: **3983** (override via `AGENT_PORT` env var).

Send a message:
```bash
cd connector-emulator
AGENT_URL=http://localhost:3983/api/messages TESTER_NAME=default-cache dotnet run
```

## Verified

- 3 messages sent sequentially -> all processed, all exported
- A365 export: 2 batches, both HTTP 200
- Token resolved from built-in cache, no custom code
- Zero errors, zero "No token" warnings

## Comparison with Custom TokenResolver

If you need full control over token acquisition (e.g., MSAL directly, certificate auth, managed identity), set `o.Agent365.Exporter.TokenResolver`:

```csharp
o.Agent365.Exporter.TokenResolver = async (agentId, tenantId) =>
{
    return await myCustomTokenProvider.GetTokenAsync(agentId, tenantId);
};
```

**Note:** When `TokenResolver` is set, the distro skips `AddAgenticTracingExporter()`. If your agent also injects `IExporterTokenCache<AgenticTokenStruct>`, you must register it manually:

```csharp
builder.Services.AddSingleton<IExporterTokenCache<AgenticTokenStruct>, AgenticTokenCache>();
```
