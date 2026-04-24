// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable SKEXP0001

namespace Agent365SemanticKernelSampleAgent.Agents;

/// <summary>
/// Demonstrates manual observability instrumentation using InvokeAgentScope,
/// InferenceScope, and ExecuteToolScope directly (without auto-instrumentation
/// middleware or SK extensions).
/// </summary>
public class ManualInstrumentationAgent
{
    private readonly Kernel _kernel;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public ManualInstrumentationAgent(Kernel kernel, IConfiguration configuration, ILogger logger)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the agent with explicit manual instrumentation for all 3 OTEL scopes:
    /// InvokeAgentScope → InferenceScope → ExecuteToolScope (per tool call).
    /// </summary>
    public async Task<Agent365AgentResponse> InvokeAgentAsync(string input, ChatHistory chatHistory, ITurnContext context)
    {
        ArgumentNullException.ThrowIfNull(chatHistory);
        ArgumentNullException.ThrowIfNull(context);

        // Build observability contracts from the turn context
        var agentDetails = BuildAgentDetails(context);
        var conversationId = context.Activity?.Conversation?.Id;
        var channelName = context.Activity?.ChannelId?.ToString();
        var request = new Request(
            inputContent: new InputMessages(new[]
            {
                new ChatMessage(MessageRole.User, new IMessagePart[] { new TextPart(input) })
            }),
            conversationId: conversationId,
            channel: channelName != null ? new Channel(channelName) : null);

        var scopeDetails = new InvokeAgentScopeDetails();
        var modelId = _configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("DeploymentName") ?? "gpt-4o-mini";
        var providerName = _configuration.GetValue<bool>("AIServices:UseAzureOpenAI") ? "Azure OpenAI" : "OpenAI";

        // ── SCOPE 1: InvokeAgentScope ── wraps the entire agent invocation
        using var invokeScope = InvokeAgentScope.Start(request, scopeDetails, agentDetails);

        try
        {
            chatHistory.AddUserMessage(input);
            var completionService = _kernel.GetRequiredService<IChatCompletionService>();
            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
            };

            string? finalResponse = null;

            // Inference + tool-call loop
            while (true)
            {
                // ── SCOPE 2: InferenceScope ── wraps the LLM call
                var inferenceDetails = new InferenceCallDetails(InferenceOperationType.Chat, modelId, providerName);
                using var inferenceScope = InferenceScope.Start(request, inferenceDetails, agentDetails);

                IReadOnlyList<ChatMessageContent> results;
                try
                {
                    results = await completionService.GetChatMessageContentsAsync(chatHistory, settings, _kernel);
                }
                catch (Exception ex)
                {
                    inferenceScope.RecordError(ex);
                    throw;
                }

                var assistantMessage = results.Last();
                chatHistory.Add(assistantMessage);

                // Record inference output
                inferenceScope.RecordOutputMessages(BuildOutputMessages(assistantMessage));
                RecordTokenUsage(inferenceScope, assistantMessage);

                // Check for tool calls
                var functionCalls = assistantMessage.Items?.OfType<FunctionCallContent>()?.ToList();
                if (functionCalls == null || functionCalls.Count == 0)
                {
                    finalResponse = assistantMessage.Content;
                    break;
                }

                // ── SCOPE 3: ExecuteToolScope ── wraps each tool execution
                foreach (var functionCall in functionCalls)
                {
                    var toolName = $"{functionCall.PluginName}-{functionCall.FunctionName}";
                    var toolArgs = functionCall.Arguments != null
                        ? JsonSerializer.Serialize(functionCall.Arguments)
                        : null;

                    var toolDetails = new ToolCallDetails(
                        toolName: toolName,
                        arguments: toolArgs,
                        toolCallId: functionCall.Id);

                    using var toolScope = ExecuteToolScope.Start(request, toolDetails, agentDetails);

                    try
                    {
                        var toolResult = await functionCall.InvokeAsync(_kernel);
                        var resultText = toolResult?.Result?.ToString() ?? string.Empty;

                        chatHistory.Add(new ChatMessageContent(
                            AuthorRole.Tool,
                            resultText,
                            metadata: new Dictionary<string, object?> { ["ChatResponseMessage.FunctionToolCallId"] = functionCall.Id }));

                        toolScope.RecordResponse(resultText);
                    }
                    catch (Exception ex)
                    {
                        toolScope.RecordError(ex);
                        _logger.LogError(ex, "Tool execution failed for {ToolName}", toolName);

                        chatHistory.Add(new ChatMessageContent(
                            AuthorRole.Tool,
                            $"Error: {ex.Message}",
                            metadata: new Dictionary<string, object?> { ["ChatResponseMessage.FunctionToolCallId"] = functionCall.Id }));
                    }
                }
            }

            // Record final output on the invoke scope
            if (finalResponse != null)
            {
                invokeScope.RecordOutputMessages(new[] { finalResponse });
            }

            return new Agent365AgentResponse
            {
                Content = finalResponse ?? "No response generated.",
                ContentType = Agent365AgentResponseContentType.Text,
            };
        }
        catch (Exception ex)
        {
            invokeScope.RecordError(ex);
            throw;
        }
    }

    private static AgentDetails BuildAgentDetails(ITurnContext context)
    {
        var agentId = context.Activity?.Recipient?.AgenticAppId ?? Guid.NewGuid().ToString();
        var tenantId = context.Activity?.Conversation?.TenantId ?? context.Activity?.Recipient?.TenantId;

        return new AgentDetails(
            agentId: agentId,
            agentName: "Agent365Agent",
            agentDescription: "A365 Semantic Kernel sample agent",
            tenantId: tenantId);
    }

    private static OutputMessages BuildOutputMessages(ChatMessageContent assistantMessage)
    {
        var parts = new List<IMessagePart>();
        if (!string.IsNullOrEmpty(assistantMessage.Content))
        {
            parts.Add(new TextPart(assistantMessage.Content));
        }

        // Include tool call requests as parts
        var toolCalls = assistantMessage.Items?.OfType<FunctionCallContent>();
        if (toolCalls != null)
        {
            foreach (var tc in toolCalls)
            {
                parts.Add(new ToolCallRequestPart(
                    name: $"{tc.PluginName}-{tc.FunctionName}",
                    id: tc.Id,
                    arguments: tc.Arguments != null
                        ? new Dictionary<string, object>(tc.Arguments.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value ?? string.Empty)))
                        : null));
            }
        }

        if (parts.Count == 0)
        {
            parts.Add(new TextPart(string.Empty));
        }

        return new OutputMessages(new[]
        {
            new OutputMessage(MessageRole.Assistant, parts)
        });
    }

    private static void RecordTokenUsage(InferenceScope scope, ChatMessageContent message)
    {
        var metadata = message.Metadata;
        if (metadata == null) return;

        if (metadata.TryGetValue("Usage", out var usageObj) && usageObj != null)
        {
            try
            {
                var usageJson = JsonSerializer.Serialize(usageObj);
                var usage = JsonSerializer.Deserialize<JsonElement>(usageJson);
                if (usage.TryGetProperty("InputTokenCount", out var inputTokens) ||
                    usage.TryGetProperty("PromptTokens", out inputTokens))
                {
                    scope.RecordInputTokens(inputTokens.GetInt32());
                }

                if (usage.TryGetProperty("OutputTokenCount", out var outputTokens) ||
                    usage.TryGetProperty("CompletionTokens", out outputTokens))
                {
                    scope.RecordOutputTokens(outputTokens.GetInt32());
                }
            }
            catch
            {
                // Token usage extraction is best-effort
            }
        }
    }
}
