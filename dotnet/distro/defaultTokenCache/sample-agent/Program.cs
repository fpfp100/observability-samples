// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Minimal distro SK sample: tests the built-in AgenticTokenCache end-to-end
// with the A365 exporter. NO custom A365OtelWrapper, NO AgentMetrics, NO custom ActivitySource.

using MinimalDistroAgent.Agents;
using Microsoft.Agents.A365.Observability.Hosting.Middleware;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
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
// NO custom TokenResolver — rely entirely on the distro's built-in AgenticTokenCache
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .Clear()
        .AddService(
            serviceName: "A365.SemanticKernel.Minimal",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Export to A365 + Console — no custom TokenResolver set,
        // so the distro will auto-register its built-in AgenticTokenCache
        o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;
    })
    .WithMetrics(metrics => metrics.AddView("*", MetricStreamConfiguration.Drop));

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddHttpClient();

// Register Semantic Kernel
builder.Services.AddKernel();

// Register the AI service — Azure OpenAI or OpenAI based on config
if (builder.Configuration.GetSection("AIServices").GetValue<bool>("UseAzureOpenAI"))
{
    builder.Services.AddAzureOpenAIChatCompletion(
        deploymentName: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("DeploymentName")!,
        endpoint: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("Endpoint")!,
        apiKey: builder.Configuration.GetSection("AIServices:AzureOpenAI").GetValue<string>("ApiKey")!);
}
else
{
    builder.Services.AddOpenAIChatCompletion(
        modelId: builder.Configuration.GetSection("AIServices:OpenAI").GetValue<string>("ModelId")!,
        apiKey: builder.Configuration.GetSection("AIServices:OpenAI").GetValue<string>("ApiKey")!);
}

// Add AgentApplicationOptions from appsettings section "AgentApplication"
builder.AddAgentApplicationOptions();

// Add the minimal agent
builder.AddAgent<MinimalAgent>();

// Register IStorage — MemoryStorage for development
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Configure the HTTP request pipeline
builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

// Always register observability middleware:
// - BaggageTurnMiddleware populates baggage context (tenant, agent, conversation, user)
//   on all spans including SK auto-instrumented ones
// - OutputLoggingMiddleware emits output_messages spans
// Both are needed so that SK auto spans get identity attributes and aren't dropped by the A365 exporter
builder.Services.AddSingleton<BaggageTurnMiddleware>();
builder.Services.AddSingleton<OutputLoggingMiddleware>();

builder.Services.AddSingleton<Microsoft.Agents.Builder.IMiddleware[]>(sp =>
{
    var scopeMiddleware = sp.GetRequiredService<BaggageTurnMiddleware>();
    var outputMiddleware = sp.GetRequiredService<OutputLoggingMiddleware>();
    return [
        scopeMiddleware,  // Scope middleware runs first
        outputMiddleware, // Output logging middleware runs second
    ];
});

WebApplication app = builder.Build();

// Enable AspNet authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Message endpoint — no custom AgentMetrics wrapper, just direct adapter processing
var incomingRoute = app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await adapter.ProcessAsync(request, response, agent, cancellationToken);
});

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => "Agent 365 Semantic Kernel Minimal Example Agent (built-in token cache test)");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();

    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3983";
    app.Urls.Add($"http://localhost:{port}");
}
else
{
    app.MapControllers();
}

app.Run();
