// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// IMPORTANT: This file must be the FIRST import in index.ts.
//
// WHY: @microsoft/agents-a365-observability's OpenTelemetryScope captures a
// static tracer via `trace.getTracer()` at module-load time. If the OTel SDK
// is not registered *before* that module loads, the ProxyTracerProvider has no
// delegate yet and all spans are created as noop (span ID = 0000000000000000),
// so nothing gets exported.
//
// Because TypeScript/CJS executes all top-level `require()` calls before any
// module body code, placing `useMicrosoftOpenTelemetry()` in a file that also
// imports `@microsoft/agents-a365-observability-hosting` is too late — that
// package transitively loads agents-a365-observability before the SDK starts.
//
// This file intentionally imports ONLY packages that do NOT transitively load
// @microsoft/agents-a365-observability (dotenv, @microsoft/opentelemetry,
// @opentelemetry/resources, ./token-cache) so the SDK is registered first.

import { configDotenv } from 'dotenv';

// Load .env before reading any process.env values below.
configDotenv();

import { useMicrosoftOpenTelemetry } from '@microsoft/opentelemetry';
import { resourceFromAttributes } from '@opentelemetry/resources';
// token-cache.ts has no agents-a365-* dependencies — safe to import here.
import { tokenResolver as customTokenResolver } from './token-cache.js';

const tokenResolverDebug = process.env.A365_TOKEN_RESOLVER_DEBUG === 'true';

// Token resolver evaluated lazily at export time (not at SDK-init time).
// By the time the exporter calls this function, dotenv is already loaded and
// all modules are cached, so dynamic require() is safe.
const otelTokenResolver = async (agentId: string, tenantId: string): Promise<string> => {
  const token = customTokenResolver(agentId, tenantId) ?? '';
  if (tokenResolverDebug) {
    console.log(`[otel-init] custom tokenResolver called for ${tenantId}/${agentId}; token=${token ? 'hit' : 'miss'}`);
  }
  return token;
};

useMicrosoftOpenTelemetry({
  resource: resourceFromAttributes({
    'service.name': 'LangChain Sample Agent',
    'service.version': '1.0.0',
  }),
  azureMonitor: {
    enabled: Boolean(process.env.APPLICATIONINSIGHTS_CONNECTION_STRING),
  },
  instrumentationOptions: {
    langchain: { isContentRecordingEnabled: true },
  },
  a365: {
    // Re-read env var at init time; dotenv has already run above.
    enabled: process.env.ENABLE_A365_OBSERVABILITY_EXPORTER !== 'false',
    tokenResolver: otelTokenResolver,
  },
});
