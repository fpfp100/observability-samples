// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// IMPORTANT: This file must be the FIRST import in index.ts.
//
// WHY: @microsoft/agents-a365-observability's OpenTelemetryScope captures a
// static tracer via `trace.getTracer()` at module-load time. If the OTel SDK
// is not registered *before* that module loads, the ProxyTracerProvider has no
// delegate yet and all spans are created as noop (span ID = 0000000000000000),
// so nothing gets exported.

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
    'service.name': 'TypeScript Sample Agent (S2S)',
    'service.version': '1.0.0',
  }),
  azureMonitor: {
    enabled: Boolean(process.env.APPLICATIONINSIGHTS_CONNECTION_STRING),
  },
  instrumentationOptions: {
    http: { enabled: false },
  },
  a365: {
    enabled: true,
    tokenResolver: otelTokenResolver,
    useS2SEndpoint: true,  // S2S uses /observabilityService endpoint
  },
});

console.log('[otel-init] S2S Agent365Exporter configured via useMicrosoftOpenTelemetry with useS2SEndpoint=true');
