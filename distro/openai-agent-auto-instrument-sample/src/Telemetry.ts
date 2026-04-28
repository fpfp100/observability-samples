// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ------------------------------------------------------------------------------

// IMPORTANT: This file MUST be the FIRST import in index.ts so that the
// OpenTelemetry SDK is registered before any module loads
// `@microsoft/opentelemetry`'s A365 scopes (which capture a static tracer at
// module-load time via `trace.getTracer()`).

import { configDotenv } from 'dotenv';
configDotenv();

import { useMicrosoftOpenTelemetry } from '@microsoft/opentelemetry';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { tokenResolver as customTokenResolver } from './token-cache';

const tokenResolverDebug = process.env.A365_TOKEN_RESOLVER_DEBUG === 'true';

const otelTokenResolver = async (agentId: string, tenantId: string): Promise<string> => {
  const token = customTokenResolver(agentId, tenantId) ?? '';
  if (tokenResolverDebug) {
    console.log(`[otel-init] custom tokenResolver called for ${tenantId}/${agentId}; token=${token ? 'hit' : 'miss'}`);
  }
  return token;
};

useMicrosoftOpenTelemetry({
  resource: resourceFromAttributes({
    'service.name': 'OpenAI Agent Instrumentation Sample',
    'service.version': '1.0.0',
  }),
  azureMonitor: {
    enabled: Boolean(process.env.APPLICATIONINSIGHTS_CONNECTION_STRING),
  },
  instrumentationOptions: {
    http: { enabled: false },
    openaiAgents: {
      tracerName: 'openai-agent-auto-instrumentation',
      tracerVersion: '1.0.0',
    },
  },
  a365: {
    enabled: process.env.ENABLE_A365_OBSERVABILITY_EXPORTER !== 'false',
    tokenResolver: otelTokenResolver,
  },
});