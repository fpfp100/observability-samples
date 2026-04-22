// ------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------------------------

import {
  TurnState,
  AgentApplication,
  MemoryStorage,
  TurnContext,
} from '@microsoft/agents-hosting';
import { ActivityTypes } from '@microsoft/agents-activity';
import {
  BaggageBuilder,
  InferenceScope,
  InvokeAgentScope,
  ExecuteToolScope,
  OutputScope,
  ToolCallDetails,
  InferenceDetails,
  InferenceOperationType,
  ServiceEndpoint,
  AgentDetails,
  InvokeAgentScopeDetails,
  CallerDetails,
  UserDetails,
  OutputResponse,
  Request,
} from '@microsoft/agents-a365-observability';
import { getObservabilityAuthenticationScope } from '@microsoft/agents-a365-runtime';
import { AgenticTokenCacheInstance, BaggageBuilderUtils } from '@microsoft/agents-a365-observability-hosting';
import tokenCache from './token-cache';

interface ConversationState {
  count: number;
}
type ApplicationTurnState = TurnState<ConversationState>;

export const agentApplication = new AgentApplication<ApplicationTurnState>({
  authorization: {
    agentic: { type: 'agentic' }, // scopes set in .env
  },
  storage: new MemoryStorage(),
});

agentApplication.onConversationUpdate('membersAdded', async (_context: TurnContext) => {
  // await context.sendActivity('Non-Digital Worker sample agent is ready.');
});

agentApplication.onActivity(
  ActivityTypes.Message,
  async (context: TurnContext, state: ApplicationTurnState) => {
    let count = state.conversation.count ?? 0;
    state.conversation.count = ++count;

    const agentInfo = resolveAgentDetails(context);
    const tenantInfo = resolveTenantDetails(context);

    const baggageScope = BaggageBuilderUtils.fromTurnContext(new BaggageBuilder(), context)
      .invokeAgentServer(context.activity.serviceUrl, 3978)
      .build();

    await baggageScope.run(async () => {
      // Refresh observability token into the shared cache (batch export mode).
      // Skipped when per-request export is enabled — token is set in OTel Context by index.ts.
      if (process.env.ENABLE_A365_OBSERVABILITY_PER_REQUEST_EXPORT?.toLowerCase() !== 'true') {
        if (process.env.Use_Custom_Resolver === 'true') {
          console.log('Using custom token resolver to set token in cache');
          /* const aauToken = await agentApplication.authorization.exchangeToken(context, 'agentic', {
            scopes: getObservabilityAuthenticationScope()
          }); */

          const aauToken = await agentApplication.authorization.exchangeToken(context, 'agentic', {

  scopes: ['api://9b975845-388f-4429-889e-eab1ef63949c/.default'] // override scopes for custom resolver

  // connection: 'alternateConnection'  // optional: override the connection used for OBO

})

          
          //log if token is valid or not. 
          if (!aauToken?.token) {
             console.error('Failed to obtain token from agentic auth handler');
           }
          const cacheKey = createAgenticTokenCacheKey(agentInfo.agentId, tenantInfo.tenantId);
          tokenCache.set(cacheKey, aauToken?.token || '');
        } else {
          await AgenticTokenCacheInstance.RefreshObservabilityToken(
            agentInfo.agentId,
            tenantInfo.tenantId,
            context,
            agentApplication.authorization,
            getObservabilityAuthenticationScope()
          );
        }
      }

      const agentDetails: AgentDetails = {
        agentId: agentInfo.agentId,
        agentName: 'Azure OpenAI Agent',
        agentDescription: 'An AI agent powered by Azure OpenAI',
        agentAUID: 'aaaaaaaa-bbbb-cccc-1111-222222222222',
        agentBlueprintId: '00001111-aaaa-2222-bbbb-3333cccc4444',
        agentEmail: 'agent@contoso.com',
        tenantId: tenantInfo.tenantId,
      };
      const scopeDetails: InvokeAgentScopeDetails = {
        endpoint: { host: context.activity.serviceUrl, port: 3978 } as ServiceEndpoint,
      };
      const request: Request = {
        conversationId: context.activity.conversation?.id ?? '__PERSONAL_CHAT_ID__',
        channel: { name: 'msteams' },
        sessionId: '__PERSONAL_CHAT_ID__',
      };
      const callerDetails: CallerDetails = {
        userDetails: {
          userId: 'bbbbbbbb-cccc-dddd-2222-333333333333',
          userName: 'Alex Wilber',
          userEmail: 'alexw@contoso.com',
          callerClientIp: '192.168.1.100',
        },
      };

      const invokeScope = InvokeAgentScope.start(
        request,
        scopeDetails,
        agentDetails,
        callerDetails,
      );

      await invokeScope.withActiveSpanAsync(async () => {
        invokeScope.recordInputMessages([context.activity.text ?? 'hi, what can you do']);

        const llmResponse = await performInference(
          context.activity.text ?? 'hi, what can you do',
          agentDetails,
          context,
          callerDetails.userDetails,
        );

        const toolResponse = await performToolCall(agentDetails, context, callerDetails.userDetails);

        // OutputScope: record output messages in a separate span (async output scenarios)
        const outputResponse: OutputResponse = {
          messages: [llmResponse, toolResponse],
        };
        const outputScope = OutputScope.start(
          request,
          outputResponse,
          agentDetails,
          callerDetails.userDetails,
        );
        outputScope.dispose();

        invokeScope.recordOutputMessages([llmResponse, toolResponse]);

        // await context.sendActivity(`${llmResponse}\n\n${toolResponse}`);
      });

      invokeScope.dispose();
    });
  }
);

async function performInference(
  prompt: string,
  agentDetails: AgentDetails,
  context: TurnContext,
  userDetails?: UserDetails,
): Promise<string> {
  const inferenceDetails: InferenceDetails = {
    operationName: InferenceOperationType.CHAT,
    model: 'gpt-4o-mini',
    providerName: 'Azure OpenAI',
    inputTokens: 33,
    outputTokens: 32,
  };

  const request: Request = {
    conversationId: context.activity.conversation?.id ?? '__PERSONAL_CHAT_ID__',
    channel: { name: 'msteams' },
    sessionId: '__PERSONAL_CHAT_ID__',
  };

  const scope = InferenceScope.start(
    request,
    inferenceDetails,
    agentDetails,
    userDetails,
  );

  try {
    return await scope.withActiveSpanAsync(async () => {
      // Simulate LLM call
      await new Promise((resolve) => setTimeout(resolve, 500));

      const response = 'Hello! I can help answer questions, provide information, assist with problem-solving, offer writing suggestions, and more. Just let me know what you need!';

      scope.recordInputMessages([prompt]);
      scope.recordOutputMessages([response]);
      scope.recordFinishReasons(['stop']);

      return response;
    });
  } catch (error) {
    scope.recordError(error as Error);
    throw error;
  } finally {
    scope.dispose();
  }
}

async function performToolCall(
  agentDetails: AgentDetails,
  context: TurnContext,
  userDetails?: UserDetails,
): Promise<string> {
  const toolDetails: ToolCallDetails = {
    toolName: 'get_weather',
    arguments: 'current location',
    toolCallId: 'bbbbbbbb-1111-2222-3333-cccccccccccc',
    toolType: 'function',
    description: 'Executing get_weather tool',
  };

  const request: Request = {
    conversationId: context.activity.conversation?.id ?? '__PERSONAL_CHAT_ID__',
    channel: { name: 'msteams' },
    sessionId: '__PERSONAL_CHAT_ID__',
  };

  const scope = ExecuteToolScope.start(
    request,
    toolDetails,
    agentDetails,
    userDetails,
  );

  try {
    return await scope.withActiveSpanAsync(async () => {
      // Simulate tool execution
      await new Promise((resolve) => setTimeout(resolve, 200));

      const response = 'The weather is sunny with a high of 75 degrees.';
      scope.recordResponse(response);
      return response;
    });
  } catch (error) {
    scope.recordError(error as Error);
    throw error;
  } finally {
    scope.dispose();
  }
}

function resolveAgentDetails(context: TurnContext): { agentId: string } {
  const agentId =
    (context.activity.recipient as { agenticAppId?: string })?.agenticAppId ||
    process.env.AGENT_ID ||
    'aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb';
  return { agentId };
}

function resolveTenantDetails(context: TurnContext): { tenantId: string } {
  const tenantId =
    (context.activity.recipient as { tenantId?: string })?.tenantId ||
    process.env.connections__serviceConnection__settings__tenantId ||
    'aaaabbbb-0000-cccc-1111-dddd2222eeee';
  return { tenantId };
}

export function createAgenticTokenCacheKey(agentId: string, tenantId?: string): string {
  return tenantId ? `agentic-token-${agentId}-${tenantId}` : `agentic-token-${agentId}`;
}
