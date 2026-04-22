// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ------------------------------------------------------------------------------

import { useMicrosoftOpenTelemetry } from '@microsoft/opentelemetry';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { AgenticTokenCacheInstance } from '@microsoft/agents-a365-observability-hosting';

// Configure observability via the Microsoft OpenTelemetry distribution.
// Replaces ObservabilityManager.configure + new OpenAIAgentsTraceInstrumentor(...).
// The distro's built-in openaiAgents instrumentation auto-patches @openai/agents.
const defaultTokenResolver = async (agentId: string, tenantId: string): Promise<string> => {
  const token = await AgenticTokenCacheInstance.getObservabilityToken(agentId, tenantId);
  return token ?? '';
};

useMicrosoftOpenTelemetry({
  resource: resourceFromAttributes({
    'service.name': 'OpenAI Agent Instrumentation Sample',
    'service.version': '1.0.0',
  }),
  instrumentationOptions: {
    openaiAgents: {
      tracerName: 'openai-agent-auto-instrumentation',
      tracerVersion: '1.0.0',
      isContentRecordingEnabled: true,
    },
  },
  a365: {
    enabled: process.env.ENABLE_A365_OBSERVABILITY_EXPORTER === 'true',
    tokenResolver: defaultTokenResolver,
  },
});
