# Basic Microsoft Agent 365 SDK Sample — S2S Token Acquisition

This sample demonstrates how to acquire observability tokens using **Server-to-Server (S2S) / client_credentials** flow, instead of the agentic user token (user_fic) flow.

## S2S vs Agentic User Token Flow

| Aspect | Agentic User Token (user_fic) | S2S (client_credentials) |
|---|---|---|
| **Grant type** | `user_fic` | `client_credentials` |
| **User context required** | Yes (user ID from activity) | No |
| **Use case** | Delegated access on behalf of a user | App-only access, background jobs, service-to-service |
| **SDK method** | `authorization.exchangeToken()` | `MsalTokenProvider.getAgenticApplicationToken()` + direct token call |

## How It Works

The S2S token acquisition in `src/agent.ts` uses two strategies, attempted in order:

### Strategy A — Full Agentic S2S Chain (requires FIC)

This mirrors the SDK's internal `getAgenticApplicationToken` + `getAgenticInstanceToken` pattern:

1. **`getAgenticApplicationToken(tenantId, agentAppInstanceId)`** — Makes a `client_credentials` grant with `fmi_path` parameter to get an app-level token for the agent instance. Uses the service connection's clientId/clientSecret.

2. **`getAgenticInstanceToken(tenantId, agentAppInstanceId)`** — Uses the app token as a `clientAssertion` to create a `ConfidentialClientApplication` with the agent instance ID, then calls `acquireTokenByClientCredential` for `api://AzureAdTokenExchange/.default`.

3. The instance token is cached for the observability exporter.

**Prerequisites for Strategy A:**
- The agent app registration must have **Federated Identity Credentials (FIC)** configured in Azure AD
- The FIC must trust the issuer of the agentic application token

### Strategy B — Simple client_credentials S2S (fallback)

If Strategy A fails (e.g., no FIC configured), this uses a standard `client_credentials` grant:

1. **`MsalTokenProvider.getAccessToken(scope)`** — Calls `acquireTokenByClientCredential` directly with the service connection's clientId/clientSecret for the observability scope (`api://9b975845-388f-4429-889e-eab1ef63949c/.default`).

**Prerequisites for Strategy B:**
- The app registration must have the **Agent365.Observability.OtelWrite** application permission granted
- An admin must have granted consent for app-only access to the observability resource

## Key Code Differences from the Base Sample

The base `basic-agent-sdk-sample` uses:
```typescript
// Agentic user token flow — requires user context from the activity
authorization: { agentic: { type: 'agentic' } }
// ...
const token = await authorization.exchangeToken(turnContext, 'agentic', { scopes: [...] });
```

This S2S sample uses:
```typescript
// S2S — no agentic authorization config needed
const provider = new MsalTokenProvider(connectionSettings);
// Step 1: getAgenticApplicationToken (client_credentials + fmi_path)
const appToken = await provider.getAgenticApplicationToken(tenantId, agentId);
// Step 2: getAgenticInstanceToken (app token as CCA assertion)
const instanceToken = await provider.getAgenticInstanceToken(tenantId, agentId);
```

## Azure AD Prerequisites

Before running, ensure your app registration has:

1. **Service connection credentials** — clientId, clientSecret, tenantId configured in `.env`
2. **For Strategy A**: Federated Identity Credentials configured on the agent app registration
3. **For Strategy B**: Admin consent for app-only access to the observability resource:
   - API Permission: `api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite` (Application type)
   - Or the `.default` scope with admin consent

## Setup

Install dependencies:

```sh
$ npm install
```

Create `.env` from the template:

```sh
$ cp .env.example .env
# Edit .env with your service connection credentials
```

Key `.env` settings:
```env
# Service connection credentials
connections__serviceConnection__settings__clientId=<your-client-id>
connections__serviceConnection__settings__clientSecret=<your-client-secret>
connections__serviceConnection__settings__tenantId=<your-tenant-id>

# Enable the A365 observability exporter
ENABLE_A365_OBSERVABILITY_EXPORTER=true
Use_Custom_Resolver=true
```

Build and run:

```sh
$ npm run build
$ npm start
```

Or for development:

```sh
$ npm run dev
```

Test with the Agents Playground:

```sh
$ npm run test-tool
```

## Token Flow Diagram

```
Service Connection                     Azure AD                    Observability Service
(clientId/secret)                      (Entra ID)
       |                                   |                              |
       |--- client_credentials + fmi_path ->|                              |
       |       (getAgenticApplicationToken) |                              |
       |<-------- app token ---------------|                              |
       |                                   |                              |
       |--- CCA(agentId, appToken)-------->|                              |
       |   acquireTokenByClientCredential  |                              |
       |       (getAgenticInstanceToken)   |                              |
       |<-------- instance token ----------|                              |
       |                                   |                              |
       |                    token cached in tokenResolver                 |
       |                                   |                              |
       |                                   |    OTel exporter sends spans  |
       |                                   |    with cached token -------->|
```

## Trademarks

*Microsoft, Windows, Microsoft Azure and/or other Microsoft products and services referenced in the documentation may be either trademarks or registered trademarks of Microsoft in the United States and/or other countries. The licenses for this project do not grant you rights to use any Microsoft names, logos, or trademarks. Microsoft's general trademark guidelines can be found at http://go.microsoft.com/fwlink/?LinkID=254653.*

## License

Copyright (c) Microsoft Corporation. All rights reserved.

Licensed under the MIT License - see the [LICENSE](../../LICENSE.md) file for details.
