# Public Configuration Surface — Agent365 Node.js Observability Packages

This document enumerates the public configuration API exposed by the observability packages of the Microsoft Agent 365 Node.js SDK, based on `main` @ `b091a8b`.

## Packages covered

1. `@microsoft/agents-a365-observability`
2. `@microsoft/agents-a365-observability-hosting`
3. `@microsoft/agents-a365-observability-extensions-openai`
4. `@microsoft/agents-a365-observability-extensions-langchain`

---

## 1. `@microsoft/agents-a365-observability`

### Entry points

| API | Purpose |
|---|---|
| `ObservabilityManager.configure(cb)` | Build-style setup; callback receives `ObservabilityBuilder` |
| `ObservabilityManager.start(opts)` | Shortcut: build + start with `BuilderOptions` |
| `ObservabilityManager.getInstance()` | Retrieve current builder |

### `ObservabilityBuilder` fluent methods

| Method | Sets |
|---|---|
| `withService(name, version?)` | `service.name` resource attribute (merged as `name-version`) |
| `withServiceNamespace(namespace)` | `service.namespace` resource attribute |
| `withTokenResolver(resolver)` | `TokenResolver(agentId, tenantId) => token` for exporter auth |
| `withClusterCategory(category)` | `ClusterCategory` enum (e.g. `prod`, `preprod`, `gov`, `dod`, `mooncake`) |
| `withExporterOptions(partial)` | Partial `Agent365ExporterOptions` (merged) |
| `withConfigurationProvider(provider)` | `IConfigurationProvider<ObservabilityConfiguration>` (multi-tenant) |
| `withCustomLogger(logger)` | `ILogger` implementation (Winston, pino, etc.) |

### `Agent365ExporterOptions`

| Property | Default |
|---|---|
| `clusterCategory` | `ClusterCategory.prod` |
| `tokenResolver` | *(none — falls back to `AgenticTokenCacheInstance`)* |
| `useS2SEndpoint` | `false` |
| `maxQueueSize` | `2048` |
| `scheduledDelayMilliseconds` | `5000` |
| `exporterTimeoutMilliseconds` | `90000` |
| `httpRequestTimeoutMilliseconds` | `30000` |
| `maxExportBatchSize` | `512` |

### `ObservabilityConfiguration` (env-var-aware; function overrides)

| Option | Env var | Default |
|---|---|---|
| `observabilityAuthenticationScopes` | `A365_OBSERVABILITY_SCOPES_OVERRIDE` (space-separated) | `['api://9b975845-388f-4429-889e-eab1ef63949c/.default']` |
| `isObservabilityExporterEnabled` | `ENABLE_A365_OBSERVABILITY_EXPORTER` (`true`/`1`/`yes`/`on`) | `false` |
| `observabilityDomainOverride` | `A365_OBSERVABILITY_DOMAIN_OVERRIDE` | `null` |
| `observabilityLogLevel` | `A365_OBSERVABILITY_LOG_LEVEL` (`none`/`info`/`warn`/`error`, pipe-combinable) | `'none'` |
| *(inherited)* `clusterCategory` | `CLUSTER_CATEGORY` | — |
| *(inherited)* `isNodeEnvDevelopment` | `NODE_ENV === 'Development'` | — |

### `PerRequestSpanProcessorConfiguration` (env-var-aware; function overrides)

| Option | Env var | Default |
|---|---|---|
| `isPerRequestExportEnabled` | `ENABLE_A365_OBSERVABILITY_PER_REQUEST_EXPORT` | `false` |
| `perRequestMaxTraces` | `A365_PER_REQUEST_MAX_TRACES` | `1000` |
| `perRequestMaxSpansPerTrace` | `A365_PER_REQUEST_MAX_SPANS_PER_TRACE` | `5000` |
| `perRequestMaxConcurrentExports` | `A365_PER_REQUEST_MAX_CONCURRENT_EXPORTS` | `20` |
| `perRequestFlushGraceMs` | `A365_PER_REQUEST_FLUSH_GRACE_MS` | `250` |
| `perRequestMaxTraceAgeMs` | `A365_PER_REQUEST_MAX_TRACE_AGE_MS` | `1800000` (30 min) |

### Other public knobs

- `setLogger(ILogger)` / `getLogger()` / `resetLogger()` — swap logger at runtime
- `BaggageBuilder` — fluent API for tenant/agent/correlation baggage on active context
- `defaultObservabilityConfigurationProvider` — singleton provider
- `defaultPerRequestSpanProcessorConfigurationProvider` — singleton provider

---

## 2. `@microsoft/agents-a365-observability-hosting`

### `ObservabilityHostingManager.configure(adapter, options)` — `ObservabilityHostingOptions`

| Option | Default |
|---|---|
| `enableBaggage` | `false` (registers `BaggageMiddleware`) |
| `enableOutputLogging` | `false` (registers `OutputLoggingMiddleware`) |

### Exposed utilities

- `AgenticTokenCache` / `AgenticTokenCacheInstance` — observability-token cache (OBO exchange)
- `BaggageMiddleware`, `OutputLoggingMiddleware`
- Constants: `A365_PARENT_SPAN_KEY`, `A365_AUTH_TOKEN_KEY`

---

## 3. `@microsoft/agents-a365-observability-extensions-openai`

### `OpenAIAgentsTraceInstrumentor(config)` — `OpenAIAgentsInstrumentationConfig`

| Option | Default |
|---|---|
| `enabled` | `false` |
| `tracerName` | `'agent365-openai-agents'` |
| `tracerVersion` | *(unset)* |
| `suppressInvokeAgentInput` | `false` (suppresses `gen_ai.input.messages` on InvokeAgent spans when `true`) |

### Configuration class

- `OpenAIObservabilityConfiguration` / `OpenAIObservabilityConfigurationOptions` — currently **identical to** `ObservabilityConfigurationOptions` (no extra knobs; reserved for future extensibility)
- `defaultOpenAIObservabilityConfigurationProvider` — singleton provider

---

## 4. `@microsoft/agents-a365-observability-extensions-langchain`

### Static API (`LangChainTraceInstrumentor`)

| Method | Purpose |
|---|---|
| `instrument(module)` | Initialize + patch `@langchain/core/callbacks/manager` |
| `enable()` / `disable()` | Toggle instrumentation (after `instrument`) |
| `resetInstance()` | Testing hook |

### Configuration

- **No public options.** Tracer (`agent365-langchain`, `1.0.0`) is hardcoded; enablement is automatic on init.
- Inherits all behavior from the core observability SDK's configuration.

---

## Environment variables (all packages)

```
NODE_ENV
CLUSTER_CATEGORY
ENABLE_A365_OBSERVABILITY_EXPORTER
ENABLE_A365_OBSERVABILITY_PER_REQUEST_EXPORT
A365_OBSERVABILITY_SCOPES_OVERRIDE
A365_OBSERVABILITY_DOMAIN_OVERRIDE
A365_OBSERVABILITY_LOG_LEVEL
A365_PER_REQUEST_MAX_TRACES
A365_PER_REQUEST_MAX_SPANS_PER_TRACE
A365_PER_REQUEST_MAX_CONCURRENT_EXPORTS
A365_PER_REQUEST_FLUSH_GRACE_MS
A365_PER_REQUEST_MAX_TRACE_AGE_MS
```
