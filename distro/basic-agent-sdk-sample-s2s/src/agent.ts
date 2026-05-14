// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { TurnState, AgentApplication, TurnContext, MemoryStorage, CloudAdapter } from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
import { PROD_OBSERVABILITY_SCOPE } from '@microsoft/agents-a365-runtime';
import { ConfidentialClientApplication } from '@azure/msal-node';
import tokenCache, { createAgenticTokenCacheKey } from './token-cache.js';
import {
  A365_PARENT_SPAN_KEY,
  InvokeAgentScope,
  InvokeAgentScopeDetails,
  InferenceScope,
  InferenceDetails,
  InferenceOperationType,
  ExecuteToolScope,
  ToolCallDetails,
  OutputScope,
  AgentDetails,
  A365Request,
  ParentSpanRef,
} from '@microsoft/opentelemetry';

/**
 * Acquires an S2S token for a given scope using the agentic identity chain.
 * Same pattern as MsalTokenProvider.getAgenticInstanceToken but with a custom scope.
 */
async function getAgenticS2SToken(
  connection: { getAgenticApplicationToken: (tenantId: string, agentId: string) => Promise<string> },
  tenantId: string,
  agentId: string,
  scopes: string[]
): Promise<string> {
  const appToken = await connection.getAgenticApplicationToken(tenantId, agentId);
  const cca = new ConfidentialClientApplication({
    auth: {
      clientId: agentId,
      clientAssertion: appToken,
      authority: `https://login.microsoftonline.com/${tenantId}`,
    }
  });
  const result = await cca.acquireTokenByClientCredential({ scopes });
  if (!result?.accessToken) {
    throw new Error('Failed to acquire S2S token');
  }
  return result.accessToken;
}

export class A365Agent extends AgentApplication<TurnState> {
  constructor() {
    super({
      startTypingTimer: true,
      storage: new MemoryStorage(),
    });

    this.onActivity(ActivityTypes.Message, async (context: TurnContext, state: TurnState) => {
      await this.handleAgentMessageActivity(context, state);
    });
  }

  async handleAgentMessageActivity(turnContext: TurnContext, state: TurnState): Promise<void> {
    const userMessage = turnContext.activity.text?.trim() || '';
    if (!userMessage) return;

    const request: A365Request = { conversationId: turnContext.activity.conversation?.id };
    const agentDetails: AgentDetails = {
      agentId: turnContext.activity.recipient?.agenticAppId || 'basic-agent',
      agentName: 'BasicA365Agent-S2S',
      tenantId: turnContext.activity.recipient?.tenantId || 'unknown',
    };

    const invokeScope = InvokeAgentScope.start(request, {}, agentDetails);
    try {
      const spanCtx = invokeScope.getSpanContext();
      turnContext.turnState.set(A365_PARENT_SPAN_KEY, {
        traceId: spanCtx.traceId, spanId: spanCtx.spanId, traceFlags: spanCtx.traceFlags,
      } as ParentSpanRef);

      await this.preloadObservabilityTokenS2S(turnContext);

      const inferenceScope = InferenceScope.start(request, {
        operationName: InferenceOperationType.CHAT, model: 'gpt-4o', providerName: 'openai',
        inputTokens: 50, outputTokens: 120, finishReasons: ['stop'],
      }, agentDetails);
      await inferenceScope.withActiveSpanAsync(async () => {
        inferenceScope.recordInputMessages([userMessage]);
        inferenceScope.recordOutputMessages(['Simulated LLM response for: ' + userMessage]);
        inferenceScope.recordInputTokens(50);
        inferenceScope.recordOutputTokens(120);
        inferenceScope.recordFinishReasons(['stop']);
      });
      inferenceScope.dispose();

      const toolScope = ExecuteToolScope.start(request, {
        toolName: 'get_weather', arguments: JSON.stringify({ city: 'Seattle' }),
        toolCallId: 'call_test_001', description: 'Get the current weather for a city', toolType: 'function',
      }, agentDetails);
      await toolScope.withActiveSpanAsync(async () => {
        toolScope.recordResponse(JSON.stringify({ weather: 'sunny', temperature: '22°C' }));
      });
      toolScope.dispose();

      const responseText = 'The weather in Seattle is sunny, 22°C.';
      const outputScope = OutputScope.start(request, { messages: [responseText] }, agentDetails);
      outputScope.recordOutputMessages([responseText]);
      outputScope.dispose();

      await turnContext.sendActivity(responseText);
    } catch (error) {
      invokeScope.recordError(error instanceof Error ? error : new Error(String(error)));
      console.error('Agent invocation error:', error);
      await turnContext.sendActivity(`Error: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      invokeScope.dispose();
    }
  }

  /**
   * S2S observability token via the agentic identity chain.
   * Uses getAgenticS2SToken — same pattern as getAgenticInstanceToken but with the observability scope.
   */
  private async preloadObservabilityTokenS2S(turnContext: TurnContext): Promise<void> {
    const agentId = turnContext?.activity?.recipient?.agenticAppId ?? '';
    const tenantId = turnContext?.activity?.recipient?.tenantId ?? '';
    if (!agentId || !tenantId) return;

    try {
      const connection = (this.adapter as CloudAdapter).connectionManager.getConnection('service_connection');
      const token = await getAgenticS2SToken(connection, tenantId, agentId, [PROD_OBSERVABILITY_SCOPE]);

      const cacheKey = createAgenticTokenCacheKey(agentId, tenantId);
      tokenCache.set(cacheKey, token);
      console.log(`[S2S] Observability token cached (length=${token.length})`);
    } catch (error) {
      const msg = error instanceof Error ? error.message : String(error);
      console.error(`[S2S] Error: ${msg.substring(0, 200)}`);
    }
  }
}

export const agentApplication = new A365Agent();
