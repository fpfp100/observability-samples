// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Agent365BaseAgentFrameworkSampleAgent.telemetry;
using Agent365BaseAgentFrameworkSampleAgent.Tools;
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.A365.Tooling.Extensions.AgentFramework.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.UserAuth;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agent365BaseAgentFrameworkSampleAgent.Agent;

public class MyAgent : AgentApplication
{
    private const string AgentWelcomeMessage = "Hello! I can help you find information based on what I can access.";
    private const string AgentHireMessage = "Thank you for hiring me! Looking forward to assisting you in your professional journey!";
    private const string AgentFarewellMessage = "Thank you for your time, I enjoyed working with you.";

    private static readonly string AgentInstructionsTemplate = """
        You are a friendly assistant that helps office workers with their daily tasks.

        The user's name is {userName}. Use their name naturally where appropriate.

        You may ask follow up questions until you have enough information to answer the user's question.

        If you are working with weather information, the following instructions apply:
        Location is a city name, 2 letter US state codes should be resolved to the full name of the United States State.
        - For current weather, Use the {{WeatherLookupTool.GetCurrentWeatherForLocation}}, you should include the current temperature, low and high temperatures, wind speed, humidity, and a short description of the weather.
        - You should use the {{DateTimePlugin.GetDateTime}} to get the current date and time.

        Otherwise you should use the tools available to you to help answer the user's questions.
        """;

    private static string GetAgentInstructions(string? userName)
    {
        string safe = string.IsNullOrWhiteSpace(userName) ? "unknown" : userName.Trim();
        safe = System.Text.RegularExpressions.Regex.Replace(safe, @"[\p{Cc}\p{Cf}]", " ").Trim();
        if (safe.Length > 64) safe = safe[..64].TrimEnd();
        if (string.IsNullOrWhiteSpace(safe)) safe = "unknown";
        return AgentInstructionsTemplate.Replace("{userName}", safe, StringComparison.Ordinal);
    }

    private readonly IChatClient? _chatClient;
    private readonly IConfiguration? _configuration;
    private readonly IExporterTokenCache<AgenticTokenStruct>? _agentTokenCache;
    private readonly ILogger<MyAgent>? _logger;
    private readonly IMcpToolRegistrationService? _toolService;
    private readonly bool _useManualInstrumentation;
    private readonly string? AgenticAuthHandlerName;
    private readonly string? OboAuthHandlerName;
    private static readonly ConcurrentDictionary<string, List<AITool>> _agentToolCache = new();

    public static bool TryGetBearerTokenForDevelopment(out string? bearerToken)
    {
        bearerToken = Environment.GetEnvironmentVariable("BEARER_TOKEN");
        return !string.IsNullOrEmpty(bearerToken);
    }

    private static bool ShouldSkipToolingOnErrors()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                          Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                          "Production";

        var skipToolingOnErrors = Environment.GetEnvironmentVariable("SKIP_TOOLING_ON_ERRORS");

        return environment.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrEmpty(skipToolingOnErrors) &&
               skipToolingOnErrors.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public MyAgent(
        AgentApplicationOptions options,
        IChatClient chatClient,
        IConfiguration configuration,
        IExporterTokenCache<AgenticTokenStruct> agentTokenCache,
        IMcpToolRegistrationService toolService,
        ILogger<MyAgent> logger) : base(options)
    {
        _chatClient = chatClient;
        _configuration = configuration;
        _agentTokenCache = agentTokenCache;
        _logger = logger;
        _toolService = toolService;

        _useManualInstrumentation = string.Equals(
            configuration.GetSection("Observability").GetValue<string>("InstrumentationMode"),
            "Manual",
            StringComparison.OrdinalIgnoreCase);

        AgenticAuthHandlerName = _configuration.GetValue<string>("AgentApplication:AgenticAuthHandlerName");
        OboAuthHandlerName = _configuration.GetValue<string>("AgentApplication:OboAuthHandlerName");

        // Greet when members are added to the conversation
        OnConversationUpdate(ConversationUpdateEvents.MembersAdded, WelcomeMessageAsync);

        var agenticHandlers = !string.IsNullOrEmpty(AgenticAuthHandlerName) ? new[] { AgenticAuthHandlerName } : Array.Empty<string>();
        var oboHandlers = !string.IsNullOrEmpty(OboAuthHandlerName) ? new[] { OboAuthHandlerName } : Array.Empty<string>();

        // Handle agent install / uninstall events
        OnActivity(ActivityTypes.InstallationUpdate, OnInstallationUpdateAsync, isAgenticOnly: true, autoSignInHandlers: agenticHandlers);
        OnActivity(ActivityTypes.InstallationUpdate, OnInstallationUpdateAsync, isAgenticOnly: false);

        // Listen for messages - MUST BE AFTER ANY OTHER MESSAGE HANDLERS
        OnActivity(ActivityTypes.Message, OnMessageAsync, isAgenticOnly: true, autoSignInHandlers: agenticHandlers);
        OnActivity(ActivityTypes.Message, OnMessageAsync, isAgenticOnly: false, autoSignInHandlers: oboHandlers);
    }

    protected async Task WelcomeMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        await AgentMetrics.InvokeObservedAgentOperation(
            "WelcomeMessage",
            turnContext,
            async () =>
            {
                foreach (ChannelAccount member in turnContext.Activity.MembersAdded)
                {
                    if (member.Id != turnContext.Activity.Recipient.Id)
                    {
                        await turnContext.SendActivityAsync(AgentWelcomeMessage);
                    }
                }
            });
    }

    protected async Task OnInstallationUpdateAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        await AgentMetrics.InvokeObservedAgentOperation(
            "InstallationUpdate",
            turnContext,
            async () =>
            {
                _logger?.LogInformation(
                    "InstallationUpdate received - Action: '{Action}', DisplayName: '{Name}', UserId: '{Id}'",
                    turnContext.Activity.Action ?? "(none)",
                    turnContext.Activity.From?.Name ?? "(unknown)",
                    turnContext.Activity.From?.Id ?? "(unknown)");

                if (turnContext.Activity.Action == InstallationUpdateActionTypes.Add)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(AgentHireMessage), cancellationToken);
                }
                else if (turnContext.Activity.Action == InstallationUpdateActionTypes.Remove)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(AgentFarewellMessage), cancellationToken);
                }
            });
    }

    protected async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        if (turnContext is null)
        {
            throw new ArgumentNullException(nameof(turnContext));
        }

        string? ObservabilityAuthHandlerName;
        string? ToolAuthHandlerName;
        if (turnContext.IsAgenticRequest())
        {
            ObservabilityAuthHandlerName = ToolAuthHandlerName = AgenticAuthHandlerName;
        }
        else
        {
            ObservabilityAuthHandlerName = ToolAuthHandlerName = OboAuthHandlerName;
        }

        await A365OtelWrapper.InvokeObservedAgentOperation(
            "MessageProcessor",
            turnContext,
            turnState,
            _agentTokenCache,
            UserAuthorization,
            ObservabilityAuthHandlerName ?? string.Empty,
            _logger,
            async () =>
            {
                if (_useManualInstrumentation)
                {
                    await OnMessageManualInstrumentationAsync(turnContext, turnState, ToolAuthHandlerName, cancellationToken);
                }
                else
                {
                    await OnMessageAutoInstrumentationAsync(turnContext, turnState, ToolAuthHandlerName, cancellationToken);
                }
            });
    }

    /// <summary>
    /// Auto instrumentation path: relies on BaggageTurnMiddleware + OutputLoggingMiddleware
    /// for observability scopes.
    /// </summary>
    private async Task OnMessageAutoInstrumentationAsync(ITurnContext turnContext, ITurnState turnState, string? toolAuthHandlerName, CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync(MessageFactory.Text("Got it - working on it..."), cancellationToken).ConfigureAwait(false);
        await turnContext.SendActivityAsync(Activity.CreateTypingActivity(), cancellationToken).ConfigureAwait(false);

        await turnContext.StreamingResponse.QueueInformativeUpdateAsync("Just a moment please..").ConfigureAwait(false);
        try
        {
            var userText = turnContext.Activity.Text?.Trim() ?? string.Empty;
            var agent = await GetClientAgent(turnContext, turnState, _toolService, toolAuthHandlerName);

            if (turnContext?.Activity?.Attachments?.Count > 0)
            {
                foreach (var attachment in turnContext.Activity.Attachments)
                {
                    if (attachment.ContentType == "application/vnd.microsoft.teams.file.download.info" && !string.IsNullOrEmpty(attachment.ContentUrl))
                    {
                        userText += $"\n\n[User has attached a file: {attachment.Name}. The file can be downloaded from {attachment.ContentUrl}]";
                    }
                }
            }

            var chatMessages = GetConversationMessages(turnState);
            chatMessages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userText));

            await foreach (var response in agent!.RunStreamingAsync(chatMessages, cancellationToken: cancellationToken))
            {
                if (response.Role == ChatRole.Assistant && !string.IsNullOrEmpty(response.Text))
                {
                    turnContext?.StreamingResponse.QueueTextChunk(response.Text);
                }
            }
            SaveConversationMessages(turnState, chatMessages);
        }
        finally
        {
            await turnContext!.StreamingResponse.EndStreamAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Manual instrumentation path: creates InvokeAgentScope, InferenceScope,
    /// and ExecuteToolScope explicitly.
    /// </summary>
    private async Task OnMessageManualInstrumentationAsync(ITurnContext turnContext, ITurnState turnState, string? toolAuthHandlerName, CancellationToken cancellationToken)
    {
        var userText = turnContext.Activity.Text?.Trim() ?? string.Empty;

        // Build observability contracts from the turn context
        var agentDetails = BuildAgentDetails(turnContext);
        var conversationId = turnContext.Activity?.Conversation?.Id;
        var channelName = turnContext.Activity?.ChannelId?.ToString();
        var request = new Request(
            inputContent: new InputMessages(new[]
            {
                new Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ChatMessage(MessageRole.User, new IMessagePart[] { new TextPart(userText) })
            }),
            conversationId: conversationId,
            channel: channelName != null ? new Channel(channelName) : null);

        var scopeDetails = new InvokeAgentScopeDetails();
        var modelId = _configuration?.GetSection("AIServices:AzureOpenAI").GetValue<string>("DeploymentName") ?? "gpt-4o-mini";
        var providerName = _configuration?.GetValue<bool>("AIServices:UseAzureOpenAI") == true ? "Azure OpenAI" : "OpenAI";

        // SCOPE 1: InvokeAgentScope - wraps the entire agent invocation
        using var invokeScope = InvokeAgentScope.Start(request, scopeDetails, agentDetails);

        try
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("Got it - working on it... (manual instrumentation)"), cancellationToken).ConfigureAwait(false);

            var agent = await GetClientAgent(turnContext, turnState, _toolService, toolAuthHandlerName);

            // SCOPE 2: InferenceScope - wraps the LLM call
            var inferenceDetails = new InferenceCallDetails(InferenceOperationType.Chat, modelId, providerName);
            using var inferenceScope = InferenceScope.Start(request, inferenceDetails, agentDetails);

            try
            {
                var chatMessages = GetConversationMessages(turnState);
                chatMessages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userText));

                var responseText = new System.Text.StringBuilder();
                await foreach (var response in agent!.RunStreamingAsync(chatMessages, cancellationToken: cancellationToken))
                {
                    if (response.Role == ChatRole.Assistant && !string.IsNullOrEmpty(response.Text))
                    {
                        turnContext.StreamingResponse.QueueTextChunk(response.Text);
                        responseText.Append(response.Text);
                    }
                }

                var finalResponse = responseText.ToString();
                inferenceScope.RecordOutputMessages(new OutputMessages(new[]
                {
                    new OutputMessage(MessageRole.Assistant, new IMessagePart[] { new TextPart(finalResponse) })
                }));

                invokeScope.RecordOutputMessages(new[] { finalResponse });

                SaveConversationMessages(turnState, chatMessages);
            }
            catch (Exception ex)
            {
                inferenceScope.RecordError(ex);
                throw;
            }
        }
        catch (Exception ex)
        {
            invokeScope.RecordError(ex);
            throw;
        }
        finally
        {
            await turnContext.StreamingResponse.EndStreamAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AIAgent?> GetClientAgent(ITurnContext context, ITurnState turnState, IMcpToolRegistrationService? toolService, string? authHandlerName)
    {
        AssertionHelpers.ThrowIfNull(_configuration!, nameof(_configuration));
        AssertionHelpers.ThrowIfNull(context, nameof(context));
        AssertionHelpers.ThrowIfNull(_chatClient!, nameof(_chatClient));

        string? accessToken = null;
        string? agentId = null;
        if (!string.IsNullOrEmpty(authHandlerName))
        {
            accessToken = await UserAuthorization.GetTurnTokenAsync(context, authHandlerName);
            agentId = Utility.ResolveAgentIdentity(context, accessToken);
        }
        else if (TryGetBearerTokenForDevelopment(out var bearerToken))
        {
            accessToken = bearerToken;
            agentId = Utility.ResolveAgentIdentity(context, accessToken!);
        }

        var displayName = context.Activity.From?.Name;

        // Create local tools
        var toolList = new List<AITool>();
        toolList.Add(AIFunctionFactory.Create(DateTimeFunctionTool.getDate));

        if (toolService != null && !string.IsNullOrEmpty(agentId))
        {
            try
            {
                string toolCacheKey = GetToolCacheKey(turnState);
                if (_agentToolCache.ContainsKey(toolCacheKey))
                {
                    var cachedTools = _agentToolCache[toolCacheKey];
                    if (cachedTools != null && cachedTools.Count > 0)
                    {
                        toolList.AddRange(cachedTools);
                    }
                }
                else
                {
                    await context.StreamingResponse.QueueInformativeUpdateAsync("Loading tools...");

                    var handlerForMcp = !string.IsNullOrEmpty(authHandlerName)
                        ? authHandlerName
                        : OboAuthHandlerName ?? AgenticAuthHandlerName ?? string.Empty;
                    var tokenOverride = string.IsNullOrEmpty(authHandlerName) ? accessToken : null;

                    var a365Tools = await toolService.GetMcpToolsAsync(agentId, UserAuthorization, handlerForMcp, context, tokenOverride).ConfigureAwait(false);

                    if (a365Tools != null && a365Tools.Count > 0)
                    {
                        toolList.AddRange(a365Tools);
                        _agentToolCache.TryAdd(toolCacheKey, [.. a365Tools]);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ShouldSkipToolingOnErrors())
                {
                    _logger?.LogWarning(ex, "Failed to register MCP tool servers. Continuing without MCP tools (SKIP_TOOLING_ON_ERRORS=true).");
                }
                else
                {
                    _logger?.LogError(ex, "Failed to register MCP tool servers.");
                    throw;
                }
            }
        }

        var toolOptions = new ChatOptions
        {
            Temperature = (float?)0.2,
            Tools = toolList
        };

        return new ChatClientAgent(_chatClient!, GetAgentInstructions(displayName))
            .AsBuilder()
            .UseOpenTelemetry(sourceName: AgentMetrics.SourceName, (cfg) => cfg.EnableSensitiveData = true)
            .Build();
    }

    private static List<Microsoft.Extensions.AI.ChatMessage> GetConversationMessages(ITurnState turnState)
    {
        string? savedMessages = turnState.Conversation.GetValue<string?>("conversation.threadInfo", () => null);
        if (!string.IsNullOrEmpty(savedMessages))
        {
            try
            {
                var messages = JsonSerializer.Deserialize<List<Microsoft.Extensions.AI.ChatMessage>>(savedMessages);
                if (messages != null) return messages;
            }
            catch
            {
                // If deserialization fails, start fresh
            }
        }
        return new List<Microsoft.Extensions.AI.ChatMessage>();
    }

    private static void SaveConversationMessages(ITurnState turnState, List<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        // Keep only the last 20 messages to avoid unbounded growth
        const int maxMessages = 20;
        if (messages.Count > maxMessages)
        {
            messages.RemoveRange(0, messages.Count - maxMessages);
        }
        var serialized = JsonSerializer.Serialize(messages);
        turnState.Conversation.SetValue("conversation.threadInfo", serialized);
    }

    private string GetToolCacheKey(ITurnState turnState)
    {
        string userToolCacheKey = turnState.User.GetValue<string?>("user.toolCacheKey", () => null) ?? "";
        if (string.IsNullOrEmpty(userToolCacheKey))
        {
            userToolCacheKey = Guid.NewGuid().ToString();
            turnState.User.SetValue("user.toolCacheKey", userToolCacheKey);
            return userToolCacheKey;
        }
        return userToolCacheKey;
    }

    private static AgentDetails BuildAgentDetails(ITurnContext context)
    {
        var agentId = context.Activity?.Recipient?.AgenticAppId ?? Guid.NewGuid().ToString();
        var tenantId = context.Activity?.Conversation?.TenantId ?? context.Activity?.Recipient?.TenantId;

        return new AgentDetails(
            agentId: agentId,
            agentName: "Agent365AgentFramework",
            agentDescription: "A365 Agent Framework sample agent (base)",
            tenantId: tenantId);
    }

    private static class Utility
    {
        public static string? ResolveAgentIdentity(ITurnContext context, string? accessToken)
        {
            if (context.Activity.IsAgenticRequest())
            {
                return context.Activity.GetAgenticInstanceId();
            }

            // For non-agentic, try to extract from token or use a fallback
            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    if (handler.CanReadToken(accessToken))
                    {
                        var jwt = handler.ReadJwtToken(accessToken);
                        var appId = jwt.Claims.FirstOrDefault(c => c.Type == "appid" || c.Type == "azp")?.Value;
                        if (!string.IsNullOrEmpty(appId)) return appId;
                    }
                }
                catch
                {
                    // Token parsing is best-effort
                }
            }

            return context.Activity?.Recipient?.Id;
        }
    }
}
