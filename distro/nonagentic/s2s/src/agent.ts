// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { TurnState, AgentApplication, TurnContext, MemoryStorage } from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
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

  /**
   * Handles incoming user messages and sends responses.
   * Creates manual instrumentation scopes: InvokeAgent, Inference, ExecuteTool, Output.
   * Stores the InvokeAgent span context in turnState so OutputLoggingMiddleware
   * links output spans as children.
   */
  async handleAgentMessageActivity(turnContext: TurnContext, state: TurnState): Promise<void> {
    const userMessage = turnContext.activity.text?.trim() || '';
    if (!userMessage) {
      return;
    }

    const request: A365Request = {
      conversationId: turnContext.activity.conversation?.id,
    };
    const invokeScopeDetails: InvokeAgentScopeDetails = {};
    const agentDetails: AgentDetails = {
      agentId: process.env.connections__serviceConnection__settings__clientId || 'basic-agent',
      agentName: 'BasicA365Agent',
      tenantId: process.env.connections__serviceConnection__settings__tenantId || 'unknown',
    };

    const invokeScope = InvokeAgentScope.start(request, invokeScopeDetails, agentDetails);
    try {
      // Store span context so OutputLoggingMiddleware links output spans as children
      const spanCtx = invokeScope.getSpanContext();
      const parentSpanRef: ParentSpanRef = {
        traceId: spanCtx.traceId,
        spanId: spanCtx.spanId,
        traceFlags: spanCtx.traceFlags,
      };
      turnContext.turnState.set(A365_PARENT_SPAN_KEY, parentSpanRef);

      // Preload observability token so the exporter can resolve agent identity
      await this.preloadObservabilityToken(turnContext);

      // InferenceScope: simulate an LLM call
      const inferenceDetails: InferenceDetails = {
        operationName: InferenceOperationType.CHAT,
        model: 'gpt-4o',
        providerName: 'openai',
        inputTokens: 50,
        outputTokens: 120,
        finishReasons: ['stop'],
      };
      const inferenceScope = InferenceScope.start(request, inferenceDetails, agentDetails);
      await inferenceScope.withActiveSpanAsync(async () => {
        inferenceScope.recordInputMessages([userMessage]);
        inferenceScope.recordOutputMessages(['Simulated LLM response for: ' + userMessage]);
        inferenceScope.recordInputTokens(50);
        inferenceScope.recordOutputTokens(120);
        inferenceScope.recordFinishReasons(['stop']);
      });
      inferenceScope.dispose();

      // ExecuteToolScope: simulate a tool call
      const toolDetails: ToolCallDetails = {
        toolName: 'get_weather',
        arguments: JSON.stringify({ city: 'Seattle' }),
        toolCallId: 'call_test_001',
        description: 'Get the current weather for a city',
        toolType: 'function',
      };
      const toolScope = ExecuteToolScope.start(request, toolDetails, agentDetails);
      await toolScope.withActiveSpanAsync(async () => {
        toolScope.recordResponse(JSON.stringify({ weather: 'sunny', temperature: '22°C' }));
      });
      toolScope.dispose();

      // OutputScope: record output message
      const responseText = 'The weather in Seattle is sunny, 22°C.';
      const outputScope = OutputScope.start(
        request,
        { messages: [responseText] },
        agentDetails,
      );
      outputScope.recordOutputMessages([responseText]);
      outputScope.dispose();

      // Send the response back to the user
      await turnContext.sendActivity(responseText);
    } catch (error) {
      invokeScope.recordError(
        error instanceof Error ? error : new Error(String(error))
      );
      console.error('Agent invocation error:', error);
      await turnContext.sendActivity(`Error: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      invokeScope.dispose();
    }
  }

  /**
   * Preloads or refreshes the Observability token using non-agentic S2S client credentials.
   */
  private async preloadObservabilityToken(turnContext: TurnContext): Promise<void> {
    // For non-agentic S2S, use the ServiceConnection's client credentials
    const clientId = process.env.connections__serviceConnection__settings__clientId ?? '';
    const tenantId = process.env.connections__serviceConnection__settings__tenantId ?? '';

    try {
      const connection = (this.adapter as any).connectionManager?.getConnection('serviceConnection');
      if (!connection) {
        console.log('[A365Agent] No service connection found, skipping S2S token preload');
        return;
      }
      const token = await connection.getAccessToken('api://9b975845-388f-4429-889e-eab1ef63949c');

      console.log(`[A365Agent] Non-agentic S2S token acquired for clientId=${clientId}, tenantId=${tenantId}`);
      const cacheKey = createAgenticTokenCacheKey(clientId, tenantId);
      tokenCache.set(cacheKey, token);
    } catch (error) {
      console.error('[A365Agent] Error acquiring non-agentic S2S token:', error);
    }
  }
}

export const agentApplication = new A365Agent();
