// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { TurnState, AgentApplication, TurnContext, MemoryStorage } from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
import {
  A365_PARENT_SPAN_KEY,
  AgenticTokenCacheInstance,
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

export class DefaultCacheAgent extends AgentApplication<TurnState> {
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
          }
        }
      })
    });

    this.onActivity(ActivityTypes.Message, async (context: TurnContext, state: TurnState) => {
      await this.handleMessage(context, state);
    });
  }

  /**
   * Handles incoming user messages.
   * Uses manual instrumentation scopes and the built-in AgenticTokenCacheInstance
   * to populate the observability token cache — no custom TokenCache needed.
   */
  async handleMessage(turnContext: TurnContext, _state: TurnState): Promise<void> {
    const userMessage = turnContext.activity.text?.trim() || '';
    if (!userMessage) {
      return;
    }

    const request: A365Request = {
      conversationId: turnContext.activity.conversation?.id,
    };
    const invokeScopeDetails: InvokeAgentScopeDetails = {};
    const agentDetails: AgentDetails = {
      agentId: turnContext.activity.recipient?.agenticAppId || 'default-cache-agent',
      agentName: 'DefaultCacheAgent',
      tenantId: turnContext.activity.recipient?.tenantId || 'unknown',
    };

    const invokeScope = InvokeAgentScope.start(request, invokeScopeDetails, agentDetails);
    try {
      // Store span context so OutputLoggingMiddleware links output spans as children.
      const spanCtx = invokeScope.getSpanContext();
      const parentSpanRef: ParentSpanRef = {
        traceId: spanCtx.traceId,
        spanId: spanCtx.spanId,
        traceFlags: spanCtx.traceFlags,
      };
      turnContext.turnState.set(A365_PARENT_SPAN_KEY, parentSpanRef);

      // Populate the built-in token cache so the A365 exporter can resolve tokens.
      // This replaces the custom TokenCache pattern — just call refreshObservabilityToken.
      await this.refreshBuiltInTokenCache(turnContext);

      // InferenceScope: simulate an LLM call (echo agent).
      const inferenceDetails: InferenceDetails = {
        operationName: InferenceOperationType.CHAT,
        model: 'echo',
        providerName: 'local',
        inputTokens: userMessage.length,
        outputTokens: userMessage.length,
        finishReasons: ['stop'],
      };
      const inferenceScope = InferenceScope.start(request, inferenceDetails, agentDetails);
      await inferenceScope.withActiveSpanAsync(async () => {
        inferenceScope.recordInputMessages([userMessage]);
        inferenceScope.recordOutputMessages(['Echo: ' + userMessage]);
        inferenceScope.recordInputTokens(userMessage.length);
        inferenceScope.recordOutputTokens(userMessage.length);
        inferenceScope.recordFinishReasons(['stop']);
      });
      inferenceScope.dispose();

      // ExecuteToolScope: simulate a tool call.
      const toolDetails: ToolCallDetails = {
        toolName: 'echo_tool',
        arguments: JSON.stringify({ input: userMessage }),
        toolCallId: 'call_echo_001',
        description: 'Echoes the user input back',
        toolType: 'function',
      };
      const toolScope = ExecuteToolScope.start(request, toolDetails, agentDetails);
      await toolScope.withActiveSpanAsync(async () => {
        toolScope.recordResponse(JSON.stringify({ echo: userMessage }));
      });
      toolScope.dispose();

      // OutputScope: record output message.
      const responseText = `Echo: ${userMessage}`;
      const outputScope = OutputScope.start(
        request,
        { messages: [responseText] },
        agentDetails,
      );
      outputScope.recordOutputMessages([responseText]);
      outputScope.dispose();

      // Send the response back to the user.
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
   * Populates the built-in AgenticTokenCacheInstance so the A365 exporter
   * can resolve tokens for this agent/tenant pair.
   *
   * This is the KEY difference from the basic sample: instead of a custom
   * TokenCache class + manual cache key management, we call the distro's
   * built-in refreshObservabilityToken which handles caching, expiry,
   * and thread-safe refresh internally.
   */
  private async refreshBuiltInTokenCache(turnContext: TurnContext): Promise<void> {
    const authorization = this.getAuthorizationSafe();

    if (!authorization) {
      console.log('[DefaultCacheAgent] Authorization not configured, skipping token refresh');
      return;
    }

    const agentId = turnContext?.activity?.recipient?.agenticAppId ?? '';
    const tenantId = turnContext?.activity?.recipient?.tenantId ?? '';

    try {
      await AgenticTokenCacheInstance.refreshObservabilityToken(
        agentId,
        tenantId,
        turnContext as any,
        authorization as any,
        undefined, // use default scopes from the cache
        DefaultCacheAgent.authHandlerName,
      );
      console.log(`[DefaultCacheAgent] Refreshed built-in token cache for agentId=${agentId}, tenantId=${tenantId}`);
    } catch (error) {
      console.error(`[DefaultCacheAgent] Failed to refresh token cache:`, error);
    }
  }

  private getAuthorizationSafe() {
    try {
      return this.authorization;
    } catch {
      return undefined;
    }
  }
}

export const agentApplication = new DefaultCacheAgent();
