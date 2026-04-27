// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { TurnState, AgentApplication, TurnContext, MemoryStorage } from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
import { getObservabilityAuthenticationScope } from '@microsoft/agents-a365-runtime';
import tokenCache, { createAgenticTokenCacheKey } from './token-cache.js';
import './client.js';
// Manual instrumentation commented out — testing auto-instrumentation only
// import {
//   A365_PARENT_SPAN_KEY,
//   InvokeAgentScope,
//   InvokeAgentScopeDetails,
//   InferenceScope,
//   InferenceDetails,
//   InferenceOperationType,
//   ExecuteToolScope,
//   ToolCallDetails,
//   OutputScope,
//   AgentDetails,
//   A365Request,
//   ParentSpanRef,
// } from '@microsoft/opentelemetry';

export class A365Agent extends AgentApplication<TurnState> {
  static authHandlerName: string = 'agentic';

  constructor() {
    const useAgenticAuth = process.env.USE_AGENTIC_AUTH === 'true';

    super({
      startTypingTimer: true,
      storage: new MemoryStorage(),
      ...(useAgenticAuth && {
        authorization: {
          agentic: {
            type: 'agentic',
          } // scopes set in the .env file or environment
        }
      })
    });

    this.onActivity(ActivityTypes.Message, async (context: TurnContext, state: TurnState) => {
      await this.handleAgentMessageActivity(context, state);
    });
  }

  /**
   * Handles incoming user messages and sends responses.
   * Creates an InvokeAgentScope and stores its span context in turnState
   * so that OutputLoggingMiddleware links output spans as children.
   */
  async handleAgentMessageActivity(turnContext: TurnContext, state: TurnState): Promise<void> {
    const userMessage = turnContext.activity.text?.trim() || '';
    if (!userMessage) {
      return;
    }

    // Manual instrumentation commented out — testing auto-instrumentation only
    // const request: A365Request = {
    //   conversationId: turnContext.activity.conversation?.id,
    // };
    // const invokeScopeDetails: InvokeAgentScopeDetails = {};
    // const agentDetails: AgentDetails = {
    //   agentId: turnContext.activity.recipient?.agenticAppId || 'langchain-agent',
    //   agentName: 'LangChainA365Agent',
    //   tenantId: turnContext.activity.recipient?.tenantId || 'unknown',
    // };
    //
    // const invokeScope = InvokeAgentScope.start(request, invokeScopeDetails, agentDetails);
    // try {
    //   const spanCtx = invokeScope.getSpanContext();
    //   const parentSpanRef: ParentSpanRef = {
    //     traceId: spanCtx.traceId,
    //     spanId: spanCtx.spanId,
    //     traceFlags: spanCtx.traceFlags,
    //   };
    //   turnContext.turnState.set(A365_PARENT_SPAN_KEY, parentSpanRef);
    //
    //   // InferenceScope: simulate an LLM call
    //   const inferenceDetails: InferenceDetails = {
    //     operationName: InferenceOperationType.CHAT,
    //     model: 'gpt-4o',
    //     providerName: 'openai',
    //     inputTokens: 50,
    //     outputTokens: 120,
    //     finishReasons: ['stop'],
    //   };
    //   const inferenceScope = InferenceScope.start(request, inferenceDetails, agentDetails);
    //   await inferenceScope.withActiveSpanAsync(async () => {
    //     inferenceScope.recordInputMessages([userMessage]);
    //     inferenceScope.recordOutputMessages(['Simulated LLM response for: ' + userMessage]);
    //     inferenceScope.recordInputTokens(50);
    //     inferenceScope.recordOutputTokens(120);
    //     inferenceScope.recordFinishReasons(['stop']);
    //   });
    //   inferenceScope.dispose();
    //
    //   // ExecuteToolScope: simulate a tool call
    //   const toolDetails: ToolCallDetails = {
    //     toolName: 'get_weather',
    //     arguments: JSON.stringify({ city: 'Seattle' }),
    //     toolCallId: 'call_test_001',
    //     description: 'Get the current weather for a city',
    //     toolType: 'function',
    //   };
    //   const toolScope = ExecuteToolScope.start(request, toolDetails, agentDetails);
    //   await toolScope.withActiveSpanAsync(async () => {
    //     toolScope.recordResponse(JSON.stringify({ weather: 'sunny', temperature: '22°C' }));
    //   });
    //   toolScope.dispose();
    //
    //   // OutputScope: simulate output message
    //   const outputScope = OutputScope.start(
    //     request,
    //     { messages: ['The weather in Seattle is sunny, 22°C.'] },
    //     agentDetails,
    //   );
    //   outputScope.recordOutputMessages(['The weather in Seattle is sunny, 22°C.']);
    //   outputScope.dispose();

    try {
      // Preload observability token so the exporter can resolve agent identity
      await this.preloadObservabilityToken(turnContext);

      const response = '[auto-instrument-test] Echo: ' + userMessage;

      // Send the response back to the user
      await turnContext.sendActivity(response);
    } catch (error) {
      // invokeScope.recordError(
      //   error instanceof Error ? error : new Error(String(error))
      // );
      console.error('Agent invocation error:', error);
      await turnContext.sendActivity(`Error: ${error instanceof Error ? error.message : String(error)}`);
    }
    // } finally {
    //   invokeScope.dispose();
    // }
  }

  /**
   * Preloads or refreshes the Observability token used by the Agent 365 Observability exporter.
   */
  private async preloadObservabilityToken(turnContext: TurnContext): Promise<void> {
    const authorization = this.getAuthorizationSafe();

    // Only attempt token preloading if authorization is configured
    if (!authorization) {
      console.log('[A365Agent] Authorization not configured, skipping observability token preload');
      return;
    }

    const agentId = turnContext?.activity?.recipient?.agenticAppId ?? '';
    const tenantId = turnContext?.activity?.recipient?.tenantId ?? '';

    const aauToken = await authorization.exchangeToken(turnContext, 'agentic', {
      scopes: ['api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite']
    });
    console.log(`Preloaded Observability token for agentId=${agentId}, tenantId=${tenantId} token=${aauToken?.token?.substring(0, 10)}...`);
    const cacheKey = createAgenticTokenCacheKey(agentId, tenantId);
    tokenCache.set(cacheKey, aauToken?.token || '');
  }

  private getAuthorizationSafe() {
    try {
      return this.authorization;
    } catch {
      return undefined;
    }
  }
}

export const agentApplication = new A365Agent();
