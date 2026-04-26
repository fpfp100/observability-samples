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
using System;
using System.Threading;

// Enable OpenAI SDK telemetry (required for OpenAI.* activity source to emit spans)
AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Setup OpenTelemetry via Microsoft.OpenTelemetry distro
builder.Services.AddOpenTelemetry()
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

if (useAutoInstrumentation)
{
    // Auto: Register the A365 Scope Middleware for auto-instrumentation
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
}
else
{
    // Manual: No observability middleware - scopes are created explicitly in the agent
    builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
    {
        return [
            new TranscriptLoggerMiddleware(new FileTranscriptLogger())
        ];
    });
}

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
    app.Urls.Add("http://localhost:3978");
}
else
{
    app.MapGet("/", () => "Agent 365 OpenAI Example Agent (Distro)");
    app.MapControllers().AllowAnonymous();
    app.Urls.Add("http://localhost:3978");
}

app.Run();
