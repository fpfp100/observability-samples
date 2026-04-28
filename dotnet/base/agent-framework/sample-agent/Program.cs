// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Agent365BaseAgentFrameworkSampleAgent;
using Agent365BaseAgentFrameworkSampleAgent.Agent;
using Agent365BaseAgentFrameworkSampleAgent.telemetry;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.A365.Observability;
using Microsoft.Agents.A365.Observability.Hosting;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;
using Microsoft.Agents.A365.Observability.Runtime;
using Microsoft.Agents.A365.Tooling.Extensions.AgentFramework.Services;
using Microsoft.Agents.A365.Tooling.Services;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.Transcript;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Setup Aspire service defaults, including OpenTelemetry, Service Discovery, Resilience, and Health Checks
builder.ConfigureOpenTelemetry();

// Load user secrets (works in any environment; secrets are simply absent in production deployments)
builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddHttpClient();

// Read instrumentation mode from config
var instrumentationMode = builder.Configuration.GetSection("Observability").GetValue<string>("InstrumentationMode") ?? "Auto";
bool useAutoInstrumentation = string.Equals(instrumentationMode, "Auto", System.StringComparison.OrdinalIgnoreCase);

// Register IChatClient - supports both AzureOpenAI and OpenAI via config toggle
if (builder.Configuration.GetSection("AIServices").GetValue<bool>("UseAzureOpenAI"))
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var confSvc = sp.GetRequiredService<IConfiguration>();
        var endpoint = confSvc["AIServices:AzureOpenAI:Endpoint"] ?? string.Empty;
        var apiKey = confSvc["AIServices:AzureOpenAI:ApiKey"] ?? string.Empty;
        var deployment = confSvc["AIServices:AzureOpenAI:DeploymentName"] ?? string.Empty;

        AssertionHelpers.ThrowIfNullOrEmpty(endpoint, "AIServices:AzureOpenAI:Endpoint configuration is missing and required.");
        AssertionHelpers.ThrowIfNullOrEmpty(apiKey, "AIServices:AzureOpenAI:ApiKey configuration is missing and required.");
        AssertionHelpers.ThrowIfNullOrEmpty(deployment, "AIServices:AzureOpenAI:DeploymentName configuration is missing and required.");

        var endpointUri = new Uri(endpoint);
        var apiKeyCredential = new AzureKeyCredential(apiKey);

        return new AzureOpenAIClient(endpointUri, apiKeyCredential)
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: AgentMetrics.SourceName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();
    });
}
else
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var confSvc = sp.GetRequiredService<IConfiguration>();
        var modelId = confSvc["AIServices:OpenAI:ModelId"] ?? string.Empty;
        var apiKey = confSvc["AIServices:OpenAI:ApiKey"] ?? string.Empty;

        AssertionHelpers.ThrowIfNullOrEmpty(modelId, "AIServices:OpenAI:ModelId configuration is missing and required.");
        AssertionHelpers.ThrowIfNullOrEmpty(apiKey, "AIServices:OpenAI:ApiKey configuration is missing and required.");

        return new OpenAI.OpenAIClient(apiKey)
            .GetChatClient(modelId)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: AgentMetrics.SourceName, configure: (cfg) => cfg.EnableSensitiveData = true)
            .Build();
    });
}

// Configure observability.
builder.Services.AddAgenticTracingExporter();

// Note: Unlike the SK sample which uses config.WithSemanticKernel(), the base observability SDK
// does not have a WithAgentFramework() extension. We use the base AddA365Tracing for both modes.
// Auto-instrumentation is handled by BaggageTurnMiddleware + OutputLoggingMiddleware below.
builder.AddA365Tracing(config => { });

// Add AgentApplicationOptions from appsettings section "AgentApplication".
builder.AddAgentApplicationOptions();

// Add the AgentApplication, which contains the logic for responding to user messages.
builder.AddAgent<MyAgent>();

// Register IStorage. For development, MemoryStorage is suitable.
// For production Agents, persisted storage should be used so
// that state survives Agent restarts, and operates correctly
// in a cluster of Agent instances.
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Register MCP tool services
builder.Services.AddSingleton<IMcpToolRegistrationService, McpToolRegistrationService>();
builder.Services.AddSingleton<IMcpToolServerConfigurationService, McpToolServerConfigurationService>();

// Configure the HTTP request pipeline.
// Add AspNet token validation for Azure Bot Service and Entra.
builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

if (useAutoInstrumentation)
{
    // Auto: Register the A365 Scope Middleware for auto-instrumentation
    // BaggageTurnMiddleware handles InvokeAgentScope; OutputLoggingMiddleware handles OutputScope
    builder.Services.AddSingleton<BaggageTurnMiddleware>();
    builder.Services.AddSingleton<OutputLoggingMiddleware>();

    builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
    {
        var scopeMiddleware = sp.GetRequiredService<BaggageTurnMiddleware>();
        var outputMiddleware = sp.GetRequiredService<OutputLoggingMiddleware>();
        return [
            scopeMiddleware,  // Scope middleware runs first
            outputMiddleware, // Output logging middleware runs second
            new TranscriptLoggerMiddleware(new FileTranscriptLogger())  // Transcript logging
        ];
    });
}
else
{
    // Manual: No observability middleware - scopes are created explicitly in ManualInstrumentationAgent
    builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
    {
        return [
            new TranscriptLoggerMiddleware(new FileTranscriptLogger())
        ];
    });
}

WebApplication app = builder.Build();

// Enable AspNet authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// This receives incoming messages from Azure Bot Service or other SDK Agents
app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await AgentMetrics.InvokeObservedHttpOperation("agent.process_message", async () =>
    {
        await adapter.ProcessAsync(request, response, agent, cancellationToken);
    }).ConfigureAwait(false);
});

// Health check endpoint for CI/CD pipelines and monitoring
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = System.DateTime.UtcNow }));

var agentPort = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3982";
app.Urls.Add($"http://localhost:{agentPort}");

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => "Agent 365 Agent Framework Example Agent (Base)");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();
}
else
{
    app.MapGet("/", () => "Agent 365 Agent Framework Example Agent (Base)");
    app.MapControllers().AllowAnonymous();
}

app.Run();
