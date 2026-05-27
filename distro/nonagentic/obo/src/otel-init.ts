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

import { useMicrosoftOpenTelemetry, Agent365Exporter, A365SpanProcessor } from '@microsoft/opentelemetry';
import { BatchSpanProcessor, type SpanProcessor } from '@opentelemetry/sdk-trace-base';
import { resourceFromAttributes } from '@opentelemetry/resources';
// token-cache.ts has no agents-a365-* dependencies — safe to import here.
import { tokenResolver as customTokenResolver } from './token-cache.js';

const tokenResolverDebug = process.env.A365_TOKEN_RESOLVER_DEBUG === 'true';

// Token resolver evaluated lazily at export time (not at SDK-init time).
const otelTokenResolver = async (agentId: string, tenantId: string): Promise<string> => {
  const token = customTokenResolver(agentId, tenantId) ?? '';
  if (tokenResolverDebug) {
    console.log(`[otel-init] custom tokenResolver called for ${tenantId}/${agentId}; token=${token ? 'hit' : 'miss'}`);
  }
  return token;
};

// Manual Agent365Exporter — OBO uses /observability endpoint (useS2SEndpoint=false)
const a365Enabled = process.env.ENABLE_A365_OBSERVABILITY_EXPORTER !== 'false';
const customSpanProcessors: SpanProcessor[] = [];

if (a365Enabled) {
  customSpanProcessors.push(new A365SpanProcessor());

  const oboExporter = new Agent365Exporter({
    tokenResolver: otelTokenResolver,
    useS2SEndpoint: false,
    clusterCategory: (process.env.CLUSTER_CATEGORY as 'prod' | 'dev' | 'test' | 'preprod') || 'prod',
  });

  customSpanProcessors.push(new BatchSpanProcessor(oboExporter));
  console.log('[otel-init] OBO Agent365Exporter configured with useS2SEndpoint=false');
}

useMicrosoftOpenTelemetry({
  resource: resourceFromAttributes({
    'service.name': 'TypeScript Sample Agent (Non-Agentic OBO)',
    'service.version': '1.0.0',
  }),
  azureMonitor: {
    enabled: Boolean(process.env.APPLICATIONINSIGHTS_CONNECTION_STRING),
  },
  instrumentationOptions: {
    http: { enabled: false },
  },
  a365: {
    enabled: false,
    tokenResolver: otelTokenResolver,
  },
  spanProcessors: customSpanProcessors,
});
