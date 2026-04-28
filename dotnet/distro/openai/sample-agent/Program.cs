// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Agent365OpenAISampleAgent;
using Agent365OpenAISampleAgent.Agent;
using Agent365OpenAISampleAgent.telemetry;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;
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
using OpenAI;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;
using System.Threading;

// Enable OpenAI SDK telemetry (required for OpenAI.* activity source to emit spans)
AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Setup OpenTelemetry via Microsoft.OpenTelemetry distro
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .Clear()
        .AddService(
            serviceName: "A365.OpenAI",
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
        }
    })
    .WithMetrics(metrics => metrics.AddView("*", MetricStreamConfiguration.Drop));

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddHttpClient();

// Read instrumentation mode from config
var instrumentationMode = builder.Configuration.GetSection("Observability").GetValue<string>("InstrumentationMode") ?? "Auto";
bool useAutoInstrumentation = string.Equals(instrumentationMode, "Auto", StringComparison.OrdinalIgnoreCase);

// Register OpenAI ChatClient directly (not through SK or Agent Framework)
if (builder.Configuration.GetSection("AIServices").GetValue<bool>("UseAzureOpenAI"))
{
    builder.Services.AddSingleton<ChatClient>(sp =>
    {
        var confSvc = sp.GetRequiredService<IConfiguration>();
        var endpoint = confSvc["AIServices:AzureOpenAI:Endpoint"] ?? string.Empty;
        var apiKey = confSvc["AIServices:AzureOpenAI:ApiKey"] ?? string.Empty;
        var deployment = confSvc["AIServices:AzureOpenAI:DeploymentName"] ?? string.Empty;

        AssertionHelpers.ThrowIfNullOrEmpty(endpoint, "AIServices:AzureOpenAI:Endpoint configuration is missing.");
        AssertionHelpers.ThrowIfNullOrEmpty(apiKey, "AIServices:AzureOpenAI:ApiKey configuration is missing.");
        AssertionHelpers.ThrowIfNullOrEmpty(deployment, "AIServices:AzureOpenAI:DeploymentName configuration is missing.");

        return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            .GetChatClient(deployment);
    });
}
else
{
    builder.Services.AddSingleton<ChatClient>(sp =>
    {
        var confSvc = sp.GetRequiredService<IConfiguration>();
        var modelId = confSvc["AIServices:OpenAI:ModelId"] ?? string.Empty;
        var apiKey = confSvc["AIServices:OpenAI:ApiKey"] ?? string.Empty;

        AssertionHelpers.ThrowIfNullOrEmpty(modelId, "AIServices:OpenAI:ModelId configuration is missing.");
        AssertionHelpers.ThrowIfNullOrEmpty(apiKey, "AIServices:OpenAI:ApiKey configuration is missing.");

        return new OpenAIClient(apiKey).GetChatClient(modelId);
    });
}

// Add AgentApplicationOptions from appsettings section "AgentApplication".
builder.AddAgentApplicationOptions();

// Add the AgentApplication
builder.AddAgent<MyAgent>();

// Register IStorage
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Configure the HTTP request pipeline.
builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

// Always register observability middleware — BaggageTurnMiddleware populates baggage context
// on all spans (including auto-instrumented ones). OutputLoggingMiddleware emits output_messages.
// Both are needed in auto AND manual modes so auto spans get identity attributes.
builder.Services.AddSingleton<BaggageTurnMiddleware>();
builder.Services.AddSingleton<OutputLoggingMiddleware>();

builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
{
    var scopeMiddleware = sp.GetRequiredService<BaggageTurnMiddleware>();
    var outputMiddleware = sp.GetRequiredService<OutputLoggingMiddleware>();
    return [
        scopeMiddleware,
        outputMiddleware,
        new TranscriptLoggerMiddleware(new FileTranscriptLogger())
    ];
});

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await AgentMetrics.InvokeObservedHttpOperation("agent.process_message", async () =>
    {
        await adapter.ProcessAsync(request, response, agent, cancellationToken);
    }).ConfigureAwait(false);
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => "Agent 365 OpenAI Example Agent (Distro)");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();
    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3980";
    app.Urls.Add($"http://localhost:{port}");
}
else
{
    app.MapGet("/", () => "Agent 365 OpenAI Example Agent (Distro)");
    app.MapControllers().AllowAnonymous();
    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3980";
    app.Urls.Add($"http://localhost:{port}");
}

app.Run();
