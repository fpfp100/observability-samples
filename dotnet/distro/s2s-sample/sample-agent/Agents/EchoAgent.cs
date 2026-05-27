// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Simple echo agent that demonstrates S2S observability token acquisition.
// Acquires an S2S token via getAgenticApplicationToken + client_credentials,
// caches it in S2STokenCache, and the distro's custom TokenResolver reads it at export time.

using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Authentication.Msal;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace S2SSampleAgent.Agents;

public class EchoAgent : AgentApplication
{
    private readonly IConnections _connections;
    private readonly ILogger<EchoAgent> _logger;
    private readonly string AgenticIdAuthHandler = "agentic";

    public EchoAgent(
        AgentApplicationOptions options,
        IConnections connections,
        ILogger<EchoAgent> logger) : base(options)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Register message handler — agentic requests get auto-sign-in
        OnActivity(ActivityTypes.Message, MessageActivityAsync, rank: RouteRank.Last, isAgenticOnly: true, autoSignInHandlers: new[] { AgenticIdAuthHandler });
        OnActivity(ActivityTypes.Message, MessageActivityAsync, rank: RouteRank.Last, isAgenticOnly: false);
    }

    /// <summary>
    /// Handles incoming messages. Populates the built-in token cache via RegisterObservability(),
    /// then echoes the user's message back with manual OTel scopes for demonstration.
    /// </summary>
    protected async Task MessageActivityAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        // Resolve agent identity
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

        string tenantId = turnContext.Activity?.Conversation?.TenantId
            ?? turnContext.Activity?.Recipient?.TenantId
            ?? Guid.Empty.ToString();

        // Set baggage context so spans get identity attributes
        using var baggageScope = new BaggageBuilder()
            .TenantId(tenantId)
            .AgentId(agentId)
            .Build();

        // KEY PART: Acquire S2S observability token and cache it.
        // Step 1: Get agentic application token via client_credentials + fmi_path
        // Step 2: Exchange for observability-scoped token via client_credentials
        // The distro's custom TokenResolver reads from S2STokenCache at export time.
        try
        {
            if (turnContext.Activity!.IsAgenticRequest())
            {
                var connection = _connections.GetConnection("ServiceConnection");

                // Get agentic application token, then exchange for observability-scoped token
                // client_credentials flow requires /.default scope
                var observabilityScopes = new[] { "api://9b975845-388f-4429-889e-eab1ef63949c/.default" };
                var msalAuth = (MsalAuth)connection;

                // Step 1: Get agentic app token via client_credentials + fmi_path
                var appToken = await msalAuth.GetAgenticApplicationTokenAsync(tenantId, agentId, cancellationToken);

                // Step 2: Use app token as client assertion to acquire observability token
                var authority = $"https://login.microsoftonline.com/{tenantId}";
                var cca = ConfidentialClientApplicationBuilder
                    .Create(agentId)
                    .WithClientAssertion(appToken)
                    .WithAuthority(authority)
                    .Build();

                var result = await cca.AcquireTokenForClient(observabilityScopes)
                    .ExecuteAsync(cancellationToken);

                S2STokenCache.Set(agentId, tenantId, result.AccessToken);
                _logger.LogInformation("[S2S] Observability token cached for agent={AgentId}, tenant={TenantId} (length={Len})",
                    agentId, tenantId, result.AccessToken.Length);
            }
            else
            {
                _logger.LogWarning("[S2S] Non-agentic request — token not acquired");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[S2S] Failed to acquire S2S observability token: {Message}", ex.Message);
        }

        // Build observability details
        var agentDetails = new AgentDetails(
            agentId: agentId,
            agentName: "S2SSampleAgent",
            agentDescription: "S2S observability echo agent sample",
            tenantId: tenantId);

        var userText = turnContext.Activity?.Text?.Trim() ?? string.Empty;
        var conversationId = turnContext.Activity?.Conversation?.Id;
        var channelName = turnContext.Activity?.ChannelId?.ToString();

        var request = new Request(
            inputContent: new InputMessages(new[]
            {
                new ChatMessage(MessageRole.User, new IMessagePart[] { new TextPart(userText) })
            }),
            conversationId: conversationId,
            channel: channelName != null ? new Channel(channelName) : null);

        // Manual OTel scope: InvokeAgentScope wraps the entire agent invocation
        using var invokeScope = InvokeAgentScope.Start(request, new InvokeAgentScopeDetails(), agentDetails);

        try
        {
            // Echo the message back
            var responseText = $"[S2S Echo] You said: {userText}";

            // Record the output on the InvokeAgentScope
            invokeScope.RecordOutputMessages(new[] { responseText });

            await turnContext.SendActivityAsync(
                MessageFactory.Text(responseText),
                cancellationToken);
        }
        catch (Exception ex)
        {
            invokeScope.RecordError(ex);
            _logger.LogError(ex, "Error processing message");
            await turnContext.SendActivityAsync(
                MessageFactory.Text($"Sorry, I encountered an error: {ex.Message}"),
                cancellationToken);
        }
    }
}
