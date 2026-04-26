// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App.UserAuth;
using Microsoft.Agents.Builder.State;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Agent365OpenAISampleAgent.telemetry;

public static class A365OtelWrapper
{
    public static async Task InvokeObservedAgentOperation(
        string operationName,
        ITurnContext turnContext,
        ITurnState turnState,
        IExporterTokenCache<AgenticTokenStruct>? agentTokenCache,
        UserAuthorization? authSystem,
        string authHandlerName,
        ILogger? logger,
        Func<Task> func)
    {
        await AgentMetrics.InvokeObservedAgentOperation(
            operationName,
            turnContext,
            async () =>
            {
                (string agentId, string tenantId) = ResolveTenantAndAgentId(turnContext);

                using var baggageScope = new BaggageBuilder()
                    .TenantId(tenantId)
                    .AgentId(agentId)
                    .Build();

                try
                {
                    if (authSystem != null)
                    {
                        agentTokenCache?.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
                            userAuthorization: authSystem,
                            turnContext: turnContext,
                            authHandlerName: authHandlerName
                        ), EnvironmentUtils.GetObservabilityAuthenticationScope());
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("There was an error registering for observability: {Message}", ex.Message);
                }

                await func().ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static (string agentId, string tenantId) ResolveTenantAndAgentId(ITurnContext turnContext)
    {
        string agentId;
        if (turnContext.Activity.IsAgenticRequest())
        {
            agentId = turnContext.Activity.GetAgenticInstanceId();
        }
        else
        {
            agentId = Guid.NewGuid().ToString();
        }
        agentId ??= Guid.Empty.ToString();

        string? tempTenantId = turnContext?.Activity?.Conversation?.TenantId ?? turnContext?.Activity?.Recipient?.TenantId;
        string tenantId = tempTenantId ?? Guid.Empty.ToString();

        return (agentId, tenantId);
    }
}
