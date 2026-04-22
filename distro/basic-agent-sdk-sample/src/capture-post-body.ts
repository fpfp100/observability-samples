// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------------------------
//
// Standalone script to capture the exact POST body exported by the Agent365 SDK.
// Usage: npx tsx src/capture-post-body.ts
//
// This spins up a local HTTP server, configures the exporter to POST there,
// creates all scope types (InvokeAgent, Inference, ExecuteTool, Output) with
// full attributes including span links, then prints the captured JSON payload.

import http from 'node:http';
import { TraceFlags } from '@opentelemetry/api';
import {
  BaggageBuilder,
  InferenceScope,
  InvokeAgentScope,
  ExecuteToolScope,
  OutputScope,
  InferenceDetails,
  InferenceOperationType,
  ToolCallDetails,
  ServiceEndpoint,
  ObservabilityManager,
  Builder,
  Agent365ExporterOptions,
  AgentDetails,
  Request,
  SpanDetails,
} from '@microsoft/agents-a365-observability';

// ── 1. Start a local HTTP server to capture the POST body ────────────────────

const captured: string[] = [];
let resolveCapture: () => void;
const capturePromise = new Promise<void>((r) => { resolveCapture = r; });

const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (chunk: Buffer) => { body += chunk.toString(); });
  req.on('end', () => {
    captured.push(body);
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status: 'ok' }));
    resolveCapture();
  });
});

server.listen(0, '127.0.0.1', async () => {
  const addr = server.address() as { port: number };
  const localEndpoint = `http://127.0.0.1:${addr.port}`;
  console.log(`Capture server listening on ${localEndpoint}\n`);

  // ── 2. Configure observability to export to local server ─────────────────

  process.env.ENABLE_OBSERVABILITY = 'true';
  process.env.ENABLE_A365_OBSERVABILITY_EXPORTER = 'true';
  process.env.A365_OBSERVABILITY_USE_CUSTOM_DOMAIN = 'true';
  process.env.A365_OBSERVABILITY_DOMAIN_OVERRIDE = localEndpoint;
  process.env.A365_OBSERVABILITY_LOG_LEVEL = 'none';

  const manager = ObservabilityManager.configure((builder: Builder) => {
    const opts = new Agent365ExporterOptions();
    opts.maxQueueSize = 10;
    builder
      .withService('TypeScript Sample Agent', '1.0.0')
      .withClusterCategory('prod' as any)
      .withExporterOptions(opts)
      .withTokenResolver((_agentId: string, _tenantId: string) => 'mock-token-for-capture');
  });

  manager.start();

  // ── 3. Create scopes with all attributes ─────────────────────────────────

  // Sample identifiers
  const agentId = '30ed5699-b157-4e87-bb45-9b0cfb13b8e5';
  const tenantId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';

  const agentDetails: AgentDetails = {
    agentId,
    agentName: 'ComplianceAssistant',
    agentDescription: 'Helps with compliance queries and policy documents',
    agentAUID: 'f47ac10b-58cc-4372-a567-0e02b2c3d479',
    agentEmail: 'compliance-agent@contoso.com',
    agentBlueprintId: 'bp-9f8e7d6c-5b4a-3210-fedc-ba0987654321',
    platformId: 'platform-agent-001',
    providerName: 'openai',
    tenantId,
  };

  // Caller agent details for Agent-to-Agent (A2A) scenarios
  const callerAgentDetails: AgentDetails = {
    agentId: 'caller-agent-1a2b3c4d',
    agentName: 'OrchestratorAgent',
    agentDescription: 'Routes compliance queries to specialized agents',
    agentAUID: 'a1234567-b890-cdef-1234-567890abcdef',
    agentEmail: 'orchestrator@contoso.com',
    agentBlueprintId: 'bp-caller-0001',
    platformId: 'caller-platform-001',
  };

  const request: Request = {
    content: 'What are the data retention policies for GDPR compliance?',
    sessionId: 'session-abc-123',
    conversationId: 'conv-001-xyz',
    channel: {
      id: 'src-meta-001',
      name: 'msteams',
      description: 'https://teams.microsoft.com/l/channel/general',
    },
  };

  // ── Span links: cross-trace causal relationships ─────────────────────────
  // These represent upstream spans that triggered this agent invocation
  const upstreamLinks = [
    {
      context: {
        traceId: '0aa4621e5ae09963a3de354f3d18aa65',
        spanId: 'c1aaa519600b1bf0',
        traceFlags: TraceFlags.SAMPLED,
      },
      attributes: { 'link.type': 'causal', 'link.reason': 'upstream_request' },
    },
    {
      context: {
        traceId: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
        spanId: 'aaaaaaaaaaaaaaaa',
        traceFlags: TraceFlags.NONE,
      },
      attributes: { 'link.type': 'follows_from', 'link.reason': 'retry' },
    },
  ];

  // Build baggage scope with all attributes including server address/port
  const baggageScope = new BaggageBuilder()
    .tenantId(tenantId)
    .agentId(agentId)
    .agentName('ComplianceAssistant')
    .agentAuid(agentDetails.agentAUID)
    .agentEmail(agentDetails.agentEmail)
    .agentBlueprintId(agentDetails.agentBlueprintId)
    .agentDescription('Helps with compliance queries and policy documents')
    .agentPlatformId('platform-agent-001')
    .sessionId('session-abc-123')
    .sessionDescription('Initial onboarding session')
    .operationSource('TypeScript Sample Agent')
    .userId('c8a7b6d5-e4f3-2110-9876-543210fedcba')
    .userName('Jane Smith')
    .userEmail('jane.smith@contoso.com')
    .callerClientIp('10.0.0.42')
    .conversationId('conv-001-xyz')
    .conversationItemLink('https://teams.microsoft.com/l/message/conv-001-xyz/msg-42')
    .channelName('msteams')
    .channelLink('https://teams.microsoft.com/l/channel/general')
    .callerAgentPlatformId('caller-platform-001')
    .invokeAgentServer('agent365.contoso.com', 8443)
    .build();

  await baggageScope.run(async () => {
    // ── InvokeAgentScope (with span links) ─────────────────────────────────
    const invokeSpanDetails: SpanDetails = {
      spanLinks: upstreamLinks,
    };

    const invokeScope = InvokeAgentScope.start(
      request,
      { endpoint: { host: 'agent365.contoso.com', port: 8443 } as ServiceEndpoint },
      agentDetails,
      { userDetails: { userId: 'c8a7b6d5-e4f3-2110-9876-543210fedcba', userName: 'Jane Smith', userEmail: 'jane.smith@contoso.com', callerClientIp: '10.0.0.42' }, callerAgentDetails },
      invokeSpanDetails,
    );

    await invokeScope.withActiveSpanAsync(async () => {
      // Record input — old schema (string[], auto-wrapped to OTEL format)
      invokeScope.recordInputMessages([
        'What are the data retention policies for GDPR compliance?',
      ]);

      // ── InferenceScope (with span link to upstream) ───────────────────
      const inferenceDetails: InferenceDetails = {
        operationName: InferenceOperationType.CHAT,
        model: 'gpt-4o',
        providerName: 'openai',
        inputTokens: 152,
        outputTokens: 287,
        finishReasons: ['stop'],
        thoughtProcess:
          'Analyzing GDPR data retention query. Checking policy documents section 4.2 and 7.1.',
        endpoint: { host: 'api.openai.com', port: 443 } as ServiceEndpoint,
      };

      const inferenceScope = InferenceScope.start(
        request,
        inferenceDetails,
        agentDetails,
        { userId: 'c8a7b6d5-e4f3-2110-9876-543210fedcba', userName: 'Jane Smith', userEmail: 'jane.smith@contoso.com' },
        { spanLinks: [upstreamLinks[0]] },
      );

      await inferenceScope.withActiveSpanAsync(async () => {
        await delay(100);

        // Record input messages
        inferenceScope.recordInputMessages([
          'You are a compliance assistant.',
          'What are the data retention policies for GDPR compliance?',
        ]);

        // Record output messages
        inferenceScope.recordOutputMessages([
          'Based on GDPR Article 5(1)(e), personal data must be kept in a form which permits identification of data subjects for no longer than is necessary.',
        ]);

        inferenceScope.recordInputTokens(152);
        inferenceScope.recordOutputTokens(287);
        inferenceScope.recordFinishReasons(['stop']);
      });
      inferenceScope.dispose();

      // ── ExecuteToolScope (no span links — tool execution is local) ────
      const toolDetails: ToolCallDetails = {
        toolName: 'search-policy-documents',
        arguments: JSON.stringify({
          query: 'GDPR data retention',
          maxResults: 5,
          filters: { region: 'EU', category: 'compliance' },
        }),
        toolCallId: 'call_abc123',
        description: 'Searches internal policy document repository',
        toolType: 'function',
        endpoint: { host: 'policy-search.contoso.com', port: 9443 } as ServiceEndpoint,
      };

      const toolScope = ExecuteToolScope.start(
        request,
        toolDetails,
        agentDetails,
      );

      await toolScope.withActiveSpanAsync(async () => {
        await delay(50);
        toolScope.recordResponse(
          JSON.stringify({
            results: [
              { title: 'GDPR Data Retention Policy v2.1', relevance: 0.95 },
              { title: 'EU Compliance Framework', relevance: 0.87 },
            ],
          }),
        );
      });
      toolScope.dispose();

      // ── OutputScope — old schema (string[]) ─────────────────────────
      const inferenceSpanContext = inferenceScope.getSpanContext();
      const outputScopeOld = OutputScope.start(
        request,
        { messages: ['Based on GDPR Article 5(1)(e), personal data must be kept for no longer than necessary.'] },
        agentDetails,
        { userId: 'c8a7b6d5-e4f3-2110-9876-543210fedcba', userName: 'Jane Smith' },
        {
          spanLinks: [{
            context: inferenceSpanContext,
            attributes: { 'link.type': 'causal', 'link.reason': 'inference_result' },
          }],
        },
      );
      outputScopeOld.recordOutputMessages([
        'Our internal policy (v2.1) specifies 3-year retention for customer data and 7-year retention for financial records.',
      ]);
      outputScopeOld.dispose();

      // ── OutputScope ─────────────────────────────────────────────────
      const outputScopeNew = OutputScope.start(
        request,
        { messages: ['Based on GDPR Article 5(1)(e), data must be kept no longer than necessary.'] },
        agentDetails,
      );
      outputScopeNew.recordOutputMessages(['Internal policy v2.1: 3-year customer data, 7-year financial records.']);
      outputScopeNew.dispose();

      // Record invoke output
      invokeScope.recordOutputMessages([
        'Based on GDPR Article 5(1)(e), personal data must be kept for no longer than necessary. Our internal policy (v2.1) specifies 3-year retention for customer data and 7-year retention for financial records.',
      ]);
    });
    invokeScope.dispose();
  });

  // ── 4. Flush and capture ───────────────────────────────────────────────────

  console.log('Flushing traces...\n');
  await manager.shutdown();

  // Wait for capture (with timeout)
  await Promise.race([capturePromise, delay(10000)]);

  server.close();

  if (captured.length === 0) {
    console.log('No POST body captured. The exporter may not have sent data.');
    process.exit(1);
  }

  for (let i = 0; i < captured.length; i++) {
    console.log(`\n${'='.repeat(80)}`);
    console.log(`POST Body #${i + 1}:`);
    console.log('='.repeat(80));
    try {
      const parsed = JSON.parse(captured[i]);
      console.log(JSON.stringify(parsed, null, 2));
    } catch {
      console.log(captured[i]);
    }
  }

  process.exit(0);
});

function delay(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}
