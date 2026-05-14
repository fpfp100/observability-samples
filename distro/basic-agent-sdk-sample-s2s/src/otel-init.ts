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

import { useMicrosoftOpenTelemetry, Agent365Exporter, A365SpanProcessor } from '@microsoft/opentelemetry';
import { BatchSpanProcessor, type SpanProcessor } from '@opentelemetry/sdk-trace-base';
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

// For S2S, we need useS2SEndpoint=true on the Agent365Exporter, which switches
// the endpoint path from /observability to /observabilityService.
// The distro's built-in a365 config doesn't expose useS2SEndpoint, so we:
//   1. Disable the distro's built-in A365 exporter (enabled: false)
//   2. Manually create an Agent365Exporter with useS2SEndpoint: true
//   3. Add it as a custom spanProcessor via the distro's spanProcessors option

const a365Enabled = process.env.ENABLE_A365_OBSERVABILITY_EXPORTER !== 'false';
const customSpanProcessors: SpanProcessor[] = [];

if (a365Enabled) {
  // A365SpanProcessor copies baggage attributes (tenant, agent, session, etc.) to spans
  customSpanProcessors.push(new A365SpanProcessor());

  // Create the Agent365Exporter with S2S endpoint enabled
  const s2sExporter = new Agent365Exporter({
    tokenResolver: otelTokenResolver,
    useS2SEndpoint: true,
    clusterCategory: (process.env.CLUSTER_CATEGORY as 'prod' | 'dev' | 'test' | 'preprod') || 'prod',
    domainOverride: process.env.A365_OBSERVABILITY_DOMAIN_OVERRIDE || undefined,
    authScopes: process.env.A365_OBSERVABILITY_SCOPES_OVERRIDE?.split(' ') || undefined,
  });

  customSpanProcessors.push(new BatchSpanProcessor(s2sExporter));
  console.log('[otel-init] S2S Agent365Exporter configured with useS2SEndpoint=true');
}

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
  // Disable the distro's built-in A365 exporter — we manage it ourselves with useS2SEndpoint
  a365: {
    enabled: false,
    tokenResolver: otelTokenResolver,
  },
  // Add our custom S2S-enabled exporter and A365SpanProcessor
  spanProcessors: customSpanProcessors,
});
