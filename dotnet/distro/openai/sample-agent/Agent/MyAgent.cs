// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Agent365OpenAISampleAgent.telemetry;
using Agent365OpenAISampleAgent.Tools;
using Microsoft.Agents.A365.Observability.Hosting.Caching;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.UserAuth;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Agent365OpenAISampleAgent.Agent;

/// <summary>
/// Sample agent that uses OpenAI ChatClient directly (not Semantic Kernel, not Agent Framework)
/// to exercise the OpenAI.* activity source for auto-instrumentation testing.
/// </summary>
public class MyAgent : AgentApplication
{
    private readonly ChatClient _chatClient;
    private readonly IConfiguration _configuration;
    private readonly IExporterTokenCache<AgenticTokenStruct>? _agentTokenCache;
    private readonly ILogger<MyAgent> _logger;
    private readonly bool _useManualInstrumentation;
    private readonly string AgenticAuthHandlerName = "agentic";

    // Define tools for the OpenAI ChatClient (function calling)
    private static readonly ChatTool GetDateTimeTool = ChatTool.CreateFunctionTool(
        functionName: "GetDateTime",
        functionDescription: "Gets the current date and time. Use this when the user asks about the current date or time.",
        functionParameters: BinaryData.FromString("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """));

    public MyAgent(
        AgentApplicationOptions options,
        ChatClient chatClient,
        IConfiguration configuration,
        ILogger<MyAgent> logger,
        IExporterTokenCache<AgenticTokenStruct>? agentTokenCache = null) : base(options)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentTokenCache = agentTokenCache;

        _useManualInstrumentation = string.Equals(
            configuration.GetSection("Observability").GetValue<string>("InstrumentationMode"),
            "Manual",
            StringComparison.OrdinalIgnoreCase);

        // Register message handler
        OnActivity(ActivityTypes.Message, OnMessageAsync, isAgenticOnly: true, autoSignInHandlers: new[] { AgenticAuthHandlerName });
        OnActivity(ActivityTypes.Message, OnMessageAsync, isAgenticOnly: false);

        // Register install/uninstall handler
        OnActivity(ActivityTypes.InstallationUpdate, OnInstallationUpdateAsync, isAgenticOnly: true, autoSignInHandlers: new[] { AgenticAuthHandlerName });
        OnActivity(ActivityTypes.InstallationUpdate, OnInstallationUpdateAsync, isAgenticOnly: false);
    }

    protected async Task OnInstallationUpdateAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        if (turnContext.Activity.Action == InstallationUpdateActionTypes.Add)
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("Thank you for hiring me! I'm an OpenAI-powered agent."), cancellationToken);
        }
        else if (turnContext.Activity.Action == InstallationUpdateActionTypes.Remove)
        {
            await turnContext.SendActivityAsync(MessageFactory.Text("Thank you for your time, I enjoyed working with you."), cancellationToken);
        }
    }

    protected async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        string authHandlerName = turnContext.IsAgenticRequest() ? AgenticAuthHandlerName : "";

        // UserAuthorization may not be configured in dev/test environments
        UserAuthorization? userAuth = null;
        try { userAuth = UserAuthorization; } catch { }

        await A365OtelWrapper.InvokeObservedAgentOperation(
            "MessageProcessor",
            turnContext,
            turnState,
            _agentTokenCache,
            userAuth,
            authHandlerName,
            _logger,
            async () =>
            {
                if (_useManualInstrumentation)
                {
                    await OnMessageManualAsync(turnContext, turnState, cancellationToken);
                }
                else
                {
                    await OnMessageAutoAsync(turnContext, turnState, cancellationToken);
                }
            });
    }

    /// <summary>
    /// Auto instrumentation path: relies on BaggageTurnMiddleware + OutputLoggingMiddleware.
    /// The OpenAI.* activity source emits spans automatically from ChatClient.
    /// </summary>
    private async Task OnMessageAutoAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        var userText = turnContext.Activity.Text?.Trim() ?? string.Empty;
        _logger.LogInformation("OpenAI Auto instrumentation - processing message: {Length} chars", userText.Length);

        var response = await InvokeOpenAIChatAsync(userText);
        await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
    }

    /// <summary>
    /// Manual instrumentation path: creates InvokeAgentScope, InferenceScope, ExecuteToolScope explicitly.
    /// </summary>
    private async Task OnMessageManualAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        var userText = turnContext.Activity.Text?.Trim() ?? string.Empty;
        _logger.LogInformation("OpenAI Manual instrumentation - processing message: {Length} chars", userText.Length);

        var agentDetails = BuildAgentDetails(turnContext);
        var conversationId = turnContext.Activity?.Conversation?.Id;
        var channelName = turnContext.Activity?.ChannelId?.ToString();
        var request = new Request(
            inputContent: new InputMessages(new[]
            {
                new Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages.ChatMessage(
                    MessageRole.User, new IMessagePart[] { new TextPart(userText) })
            }),
            conversationId: conversationId,
            channel: channelName != null ? new Channel(channelName) : null);

        var modelId = _configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("DeploymentName") ?? "gpt-4o-mini";
        var providerName = _configuration.GetValue<bool>("AIServices:UseAzureOpenAI") ? "Azure OpenAI" : "OpenAI";

        // SCOPE 1: InvokeAgentScope
        var scopeDetails = new InvokeAgentScopeDetails();
        using var invokeScope = InvokeAgentScope.Start(request, scopeDetails, agentDetails);

        try
        {
            // SCOPE 2: InferenceScope
            var inferenceDetails = new InferenceCallDetails(InferenceOperationType.Chat, modelId, providerName);
            using var inferenceScope = InferenceScope.Start(request, inferenceDetails, agentDetails);

            try
            {
                var messages = new List<OpenAI.Chat.ChatMessage>
                {
                    new SystemChatMessage("You are a helpful assistant. Use the available tools when appropriate."),
                    new UserChatMessage(userText)
                };

                var options = new ChatCompletionOptions();
                options.Tools.Add(GetDateTimeTool);

                var completion = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
                var responseText = await HandleToolCallsAsync(messages, options, completion.Value, agentDetails, request, cancellationToken);

                // Record on inference scope
                inferenceScope.RecordOutputMessages(new OutputMessages(new[]
                {
                    new OutputMessage(MessageRole.Assistant, new IMessagePart[] { new TextPart(responseText) })
                }));

                if (completion.Value.Usage != null)
                {
                    inferenceScope.RecordInputTokens(completion.Value.Usage.InputTokenCount);
                    inferenceScope.RecordOutputTokens(completion.Value.Usage.OutputTokenCount);
                }

                // Record on invoke scope
                invokeScope.RecordOutputMessages(new[] { responseText });

                await turnContext.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
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
    }

    /// <summary>
    /// Calls OpenAI ChatClient directly and handles tool call loops.
    /// This is the core path that exercises the OpenAI.* activity source.
    /// </summary>
    private async Task<string> InvokeOpenAIChatAsync(string userText)
    {
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage("You are a helpful assistant. Use the available tools when appropriate."),
            new UserChatMessage(userText)
        };

        var options = new ChatCompletionOptions();
        options.Tools.Add(GetDateTimeTool);

        var completion = await _chatClient.CompleteChatAsync(messages, options);
        return await HandleToolCallsAsync(messages, options, completion.Value, null, null, default);
    }

    /// <summary>
    /// Handles the tool call loop: if the model requests tool calls, execute them and re-prompt.
    /// In manual mode, creates ExecuteToolScope for each tool call.
    /// </summary>
    private async Task<string> HandleToolCallsAsync(
        List<OpenAI.Chat.ChatMessage> messages,
        ChatCompletionOptions options,
        ChatCompletion completion,
        AgentDetails? agentDetails,
        Request? request,
        CancellationToken cancellationToken)
    {
        const int maxToolRounds = 5;
        int round = 0;

        while (completion.FinishReason == ChatFinishReason.ToolCalls && round < maxToolRounds)
        {
            round++;

            // Add assistant message with tool calls
            messages.Add(new AssistantChatMessage(completion));

            foreach (var toolCall in completion.ToolCalls)
            {
                string toolResult;

                if (_useManualInstrumentation && agentDetails != null && request != null)
                {
                    // SCOPE 3: ExecuteToolScope (manual mode)
                    var toolDetails = new ToolCallDetails(
                        toolName: toolCall.FunctionName,
                        arguments: toolCall.FunctionArguments?.ToString(),
                        toolCallId: toolCall.Id);
                    using var toolScope = ExecuteToolScope.Start(request, toolDetails, agentDetails);
                    try
                    {
                        toolResult = ExecuteTool(toolCall);
                        toolScope.RecordResponse(toolResult);
                    }
                    catch (Exception ex)
                    {
                        toolScope.RecordError(ex);
                        toolResult = $"Error: {ex.Message}";
                    }
                }
                else
                {
                    toolResult = ExecuteTool(toolCall);
                }

                messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
            }

            // Re-prompt with tool results
            completion = (await _chatClient.CompleteChatAsync(messages, options, cancellationToken)).Value;
        }

        return completion.Content?.Count > 0 ? completion.Content[0].Text : "I couldn't generate a response.";
    }

    private static string ExecuteTool(ChatToolCall toolCall)
    {
        return toolCall.FunctionName switch
        {
            "GetDateTime" => DateTimeFunctionTool.GetDate(),
            _ => $"Unknown tool: {toolCall.FunctionName}"
        };
    }

    private static AgentDetails BuildAgentDetails(ITurnContext context)
    {
        var agentId = context.Activity?.Recipient?.AgenticAppId ?? Guid.NewGuid().ToString();
        var tenantId = context.Activity?.Conversation?.TenantId ?? context.Activity?.Recipient?.TenantId;

        return new AgentDetails(
            agentId: agentId,
            agentName: "Agent365OpenAI",
            agentDescription: "A365 OpenAI sample agent (distro - direct ChatClient)",
            tenantId: tenantId);
    }
}
