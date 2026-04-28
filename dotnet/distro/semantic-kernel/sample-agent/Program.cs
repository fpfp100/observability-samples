// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Agent365SemanticKernelSampleAgent.Agents;
using Agent365SemanticKernelSampleAgent.telemetry;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;
using Microsoft.Agents.A365.Tooling.Extensions.SemanticKernel.Services;
using Microsoft.Agents.A365.Tooling.Services;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.Agents.Storage.Transcript;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenTelemetry;
using Microsoft.SemanticKernel;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;
using System.Threading;


WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Setup OpenTelemetry via Microsoft.OpenTelemetry distro
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .Clear()
        .AddService(
            serviceName: "A365.SemanticKernel",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365;

        if (builder.Environment.IsDevelopment())
        {
            o.Exporters |= ExportTarget.Console;
            //o.Instrumentation.EnableMetrics = false;
        }
    })
    .WithMetrics(metrics => metrics.AddView("*", MetricStreamConfiguration.Drop));

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddHttpClient();

// Register Semantic Kernel
builder.Services.AddKernel();

// Register the AI service of your choice. AzureOpenAI and OpenAI are demonstrated...
if (builder.Configuration.GetSection("AIServices").GetValue<bool>("UseAzureOpenAI"))
{
    builder.Services.AddAzureOpenAIChatCompletion(
        deploymentName: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("DeploymentName")!,
        endpoint: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("Endpoint")!,
        apiKey: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("ApiKey")!);

    //Use the Azure CLI (for local) or Managed Identity (for Azure running app) to authenticate to the Azure OpenAI service
    //credentials: new ChainedTokenCredential(
    //   new AzureCliCredential(),
    //   new ManagedIdentityCredential()
    //));
}
else
{
    builder.Services.AddOpenAIChatCompletion(
        modelId: builder.Configuration.GetSection("AIServices:OpenAI").GetValue<string>("ModelId")!,
        apiKey: builder.Configuration.GetSection("AIServices:OpenAI").GetValue<string>("ApiKey")!);
}

// Read instrumentation mode from config
var instrumentationMode = builder.Configuration.GetSection("Observability").GetValue<string>("InstrumentationMode") ?? "Auto";
bool useAutoInstrumentation = string.Equals(instrumentationMode, "Auto", System.StringComparison.OrdinalIgnoreCase);


// Add AgentApplicationOptions from appsettings section "AgentApplication".
builder.AddAgentApplicationOptions();

// Add the AgentApplication, which contains the logic for responding to
// user messages.
builder.AddAgent<MyAgent>();

// Register IStorage.  For development, MemoryStorage is suitable.
// For production Agents, persisted storage should be used so
// that state survives Agent restarts, and operates correctly
// in a cluster of Agent instances.
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Register MCP tool services
builder.Services.AddSingleton<IMcpToolRegistrationService, McpToolRegistrationService>();
builder.Services.AddSingleton<IMcpToolServerConfigurationService, McpToolServerConfigurationService>();

// Configure the HTTP request pipeline.
// Add AspNet token validation for Azure Bot Service and Entra.  Authentication is configured in the appsettings.json "TokenValidation" section.
builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

// Always register observability middleware — BaggageTurnMiddleware populates baggage context
// (tenant, agent, conversation, user) on all spans including SK auto-instrumented ones.
// OutputLoggingMiddleware emits output_messages spans. Both are needed in auto AND manual modes
// so that SK auto spans get identity attributes and aren't dropped by the A365 exporter.
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
WebApplication app = builder.Build();

// Enable AspNet authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// This receives incoming messages from Azure Bot Service or other SDK Agents
var incomingRoute = app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await AgentMetrics.InvokeObservedHttpOperation("agent.process_message", async () =>
    {        
        await adapter.ProcessAsync(request, response, agent, cancellationToken);
    }).ConfigureAwait(false);
});

// Health check endpoint for CI/CD pipelines and monitoring
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = System.DateTime.UtcNow }));

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => "Agent 365 Semantic Kernel Example Agent");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();

    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3978";
    app.Urls.Add($"http://localhost:{port}");
}
else
{
    app.MapControllers();
}
app.Run();
