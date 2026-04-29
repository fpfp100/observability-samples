// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// IMPORTANT: This file must be the FIRST import in index.ts.
//
// The OTel SDK must be registered before any module that transitively loads
// @microsoft/opentelemetry scopes, otherwise spans are created as noop.
// This file intentionally imports ONLY packages that do NOT transitively load
// scope modules so the SDK is registered first.

import { configDotenv } from 'dotenv';

// Load .env before reading any process.env values below.
configDotenv();

import { useMicrosoftOpenTelemetry, AgenticTokenCacheInstance } from '@microsoft/opentelemetry';
import { resourceFromAttributes } from '@opentelemetry/resources';

// Use the built-in AgenticTokenCacheInstance as the token resolver.
// No custom TokenCache class needed — the distro provides it out of the box.
const otelTokenResolver = async (agentId: string, tenantId: string): Promise<string> => {
  const token = AgenticTokenCacheInstance.getObservabilityToken(agentId, tenantId) ?? '';
  if (process.env.A365_TOKEN_RESOLVER_DEBUG === 'true') {
    console.log(`[otel-init] AgenticTokenCacheInstance.getObservabilityToken(${tenantId}/${agentId}); token=${token ? 'hit' : 'miss'}`);
  }
  return token;
};

useMicrosoftOpenTelemetry({
  resource: resourceFromAttributes({
    'service.name': 'Default Token Cache Sample',
    'service.version': '1.0.0',
  }),
  azureMonitor: {
    enabled: Boolean(process.env.APPLICATIONINSIGHTS_CONNECTION_STRING),
  },
  instrumentationOptions: {
    http: { enabled: false },
  },
  a365: {
    enabled: process.env.ENABLE_A365_OBSERVABILITY_EXPORTER !== 'false',
    tokenResolver: otelTokenResolver,
  },
});
