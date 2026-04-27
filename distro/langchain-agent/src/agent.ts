// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { TurnState, Authorization, AgentApplication, TurnContext, MemoryStorage } from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
import { getObservabilityAuthenticationScope } from '@microsoft/agents-a365-runtime';
import tokenCache, { createAgenticTokenCacheKey } from './token-cache.js';
import { Client, getClient } from './client.js';
// Manual instrumentation commented out — testing auto-instrumentation only
// import {
//   A365_PARENT_SPAN_KEY,
//   InvokeAgentScope,
//   InvokeAgentScopeDetails,
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
  async handleAgentMessageActivity(turnContext: TurnContext, _state: TurnState): Promise<void> {
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

    try {
      // Preload observability token so the exporter can resolve agent identity
      await this.preloadObservabilityToken(turnContext);

      const client: Client = await getClient();
      const response = await client.invokeAgent(userMessage);

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

    const observabilityScopes = ['api://9b975845-388f-4429-889e-eab1ef63949c/Agent365.Observability.OtelWrite'];
    const aauToken = await authorization.exchangeToken(turnContext, 'agentic', {
      scopes: observabilityScopes
    });
    console.log(`Preloaded Observability token for agentId=${agentId}, tenantId=${tenantId} token=${aauToken?.token?.substring(0, 10)}...`);
    const cacheKey = createAgenticTokenCacheKey(agentId, tenantId);
    tokenCache.set(cacheKey, aauToken?.token || '');
  }

  private getAuthorizationSafe(): Authorization | undefined {
    try {
      return this.authorization as Authorization;
    } catch {
      return undefined;
    }
  }
}

export const agentApplication = new A365Agent();
