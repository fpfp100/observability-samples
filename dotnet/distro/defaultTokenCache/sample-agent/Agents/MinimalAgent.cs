// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Minimal agent that relies ENTIRELY on auto-instrumentation from the distro.
// NO manual InvokeAgentScope/InferenceScope — just SK ChatCompletionService + RegisterObservability().

using MinimalDistroAgent.Plugins;
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MinimalDistroAgent.Agents;

public class MinimalAgent : AgentApplication
{
    private readonly Kernel _kernel;
    private readonly IExporterTokenCache<AgenticTokenStruct> _agentTokenCache;
    private readonly ILogger<MinimalAgent> _logger;
    private readonly string AgenticIdAuthHandler = "agentic";

    public MinimalAgent(
        AgentApplicationOptions options,
        IConfiguration configuration,
        Kernel kernel,
        IExporterTokenCache<AgenticTokenStruct> agentTokenCache,
        ILogger<MinimalAgent> logger) : base(options)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _agentTokenCache = agentTokenCache ?? throw new ArgumentNullException(nameof(agentTokenCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Register DateTimePlugin for tool-call testing (generates ExecuteToolScope spans)
        _kernel.ImportPluginFromType<DateTimePlugin>();

        // Register message handler for all channels
        OnActivity(ActivityTypes.Message, MessageActivityAsync, rank: RouteRank.Last, isAgenticOnly: true, autoSignInHandlers: new[] { AgenticIdAuthHandler });
        OnActivity(ActivityTypes.Message, MessageActivityAsync, rank: RouteRank.Last, isAgenticOnly: false);
    }

    /// <summary>
    /// Handles incoming messages. Populates the built-in token cache via RegisterObservability(),
    /// then calls SK ChatCompletionService directly. All telemetry is auto-instrumented.
    /// </summary>
    protected async Task MessageActivityAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        // Resolve identity for the built-in token cache
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

        // Set baggage context so auto-instrumented spans get identity attributes
        using var baggageScope = new BaggageBuilder()
            .TenantId(tenantId)
            .AgentId(agentId)
            .Build();

        // KEY PART: Populate the distro's built-in AgenticTokenCache
        // This is what we're testing — the auto-registered cache needs to be populated
        // by agent code so the A365 exporter can authenticate when exporting spans.
        try
        {
            string authHandlerName = turnContext.Activity.IsAgenticRequest()
                ? AgenticIdAuthHandler
                : "";

            if (!string.IsNullOrEmpty(authHandlerName))
            {
                _agentTokenCache.RegisterObservability(agentId, tenantId, new AgenticTokenStruct(
                    userAuthorization: UserAuthorization,
                    turnContext: turnContext,
                    authHandlerName: authHandlerName
                ), EnvironmentUtils.GetObservabilityAuthenticationScope());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to register observability token cache: {Message}", ex.Message);
        }

        // Call SK ChatCompletionService directly — auto-instrumentation handles all spans
        var chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(
            "You are a helpful assistant. You have access to a DateTimePlugin that can tell you the current date and time. " +
            "Use it when asked about the current time or date.");
        chatHistory.AddUserMessage(turnContext.Activity.Text ?? "Hello");

        try
        {
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var result = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel,
                cancellationToken);

            await turnContext.SendActivityAsync(
                MessageFactory.Text(result.Content ?? "I processed your message but have no response."),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling chat completion service");
            await turnContext.SendActivityAsync(
                MessageFactory.Text($"Sorry, I encountered an error: {ex.Message}"),
                cancellationToken);
        }
    }
}
