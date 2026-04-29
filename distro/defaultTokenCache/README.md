# Default Token Cache Sample (Node.js Distro)

Demonstrates using the distro's **built-in `AgenticTokenCacheInstance`** for A365 observability export — no custom token cache class.

## How It Works

The distro exports `AgenticTokenCacheInstance` from `@microsoft/opentelemetry`. The developer:
1. Passes `AgenticTokenCacheInstance.getObservabilityToken` as the `tokenResolver` to `useMicrosoftOpenTelemetry()`
2. Calls `AgenticTokenCacheInstance.refreshObservabilityToken()` in the message handler to populate the cache

```
Message arrives
  → agent calls refreshObservabilityToken(agentId, tenantId, turnContext, authorization)
    → First call: OBO token exchange via authorization.exchangeToken() → token cached
    → Subsequent calls: cache hit, no exchange
  → A365 exporter fires (5s batch timer)
    → calls getObservabilityToken(agentId, tenantId) → cache hit → export
```

## Setup

### 1. otel-init.ts — Wire the built-in cache

```typescript
import { useMicrosoftOpenTelemetry, AgenticTokenCacheInstance } from '@microsoft/opentelemetry';

useMicrosoftOpenTelemetry({
  a365: {
    tokenResolver: (agentId: string, tenantId: string) =>
      AgenticTokenCacheInstance.getObservabilityToken(agentId, tenantId),
  },
});
```

### 2. agent.ts — Populate the cache in message handler

```typescript
import { AgenticTokenCacheInstance } from '@microsoft/opentelemetry';

// In your message handler:
await AgenticTokenCacheInstance.refreshObservabilityToken(
  agentId,
  tenantId,
  turnContext,
  authorization,        // from agentApplication.authorization
  undefined,            // use default scopes
  'agentic',            // auth handler name
);
```

### 3. That's it

No custom `TokenCache` class, no `createAgenticTokenCacheKey()`, no manual cache management.

## Token Cache Behavior

| Scenario | What happens |
|----------|-------------|
| First message | Cache miss → OBO token exchange → token cached |
| Subsequent messages (same pair) | Cache hit → reuses cached token |
| Token expired | Auto-detected via JWT `exp` claim → re-exchanges on next refresh |
| Export time | Exporter calls `getObservabilityToken()` → returns cached token |
| No token available | Export skipped, error logged |
| Max cache size | 10,000 entries with LRU eviction |

## Running

```bash
cd distro/defaultTokenCache
npm install
npx tsc
PORT=4006 node dist/index.js
```

Send a message:
```bash
cd connector-emulator
AGENT_URL=http://localhost:4006/api/messages TESTER_NAME=nodejs-default-cache dotnet run
```

## Verified

- Message sent → agent responded (echo)
- Token cache: `[AgenticTokenCache] Exchanging token attempt 1/3` → `Token cached`
- Token resolver: `getObservabilityToken(...); token=hit`
- A365 export: 18 spans total, 4 genAI spans exported, succeeded in 14.7s
- Zero errors

## Comparison with Custom Token Cache

The existing `distro/basic-agent-sdk-sample` uses a hand-rolled `TokenCache` class in `token-cache.ts` with manual cache key management. This sample eliminates all of that by using the distro's built-in `AgenticTokenCacheInstance`.

| | This sample | basic-agent-sdk-sample |
|---|---|---|
| Token cache | `AgenticTokenCacheInstance` (built-in) | Custom `TokenCache` class |
| Cache key management | Automatic (`agentId:tenantId`) | Manual `createAgenticTokenCacheKey()` |
| Token expiry | Auto-detected from JWT `exp` | Manual TTL |
| LRU eviction | Built-in (10k max) | None |
| Retry on failure | 3 attempts with backoff | None |
| Code needed | 2 lines (refresh + resolver) | ~40 lines (class + resolver + key fn) |
