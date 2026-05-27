// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// Minimal S2S (Server-to-Server) distro sample: demonstrates a custom TokenResolver
// with UseS2SEndpoint=true for the A365 observability exporter.
// This is a simple echo agent — no AI service — with manual OTel scopes.

using S2SSampleAgent;
using S2SSampleAgent.Agents;
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
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Setup OpenTelemetry via Microsoft.OpenTelemetry distro
// S2S requires a custom TokenResolver — the agent caches the S2S token in
// S2STokenCache and the resolver retrieves it at export time.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .Clear()
        .AddService(
            serviceName: "A365.S2S.Sample",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        // Export to A365 + Console
        o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;

        // Custom S2S token resolver — reads from the in-memory cache
        // populated by the agent's message handler
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId) =>
            Task.FromResult(S2STokenCache.Get(agentId, tenantId) ?? string.Empty);

        // Use S2S (service-to-service) endpoint — switches the exporter path
        // from /observability to /observabilityService
        o.Agent365.Exporter.UseS2SEndpoint = true;
    })
    .WithMetrics(metrics => metrics.AddView("*", MetricStreamConfiguration.Drop));


if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddHttpClient();

// Add AgentApplicationOptions from appsettings section "AgentApplication"
builder.AddAgentApplicationOptions();

// Add the S2S echo agent
builder.AddAgent<EchoAgent>();

// Register IStorage — MemoryStorage for development
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Configure the HTTP request pipeline
builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

// Register observability middleware:
// - BaggageTurnMiddleware populates baggage context (tenant, agent, conversation, user)
//   on all spans so they aren't dropped by the A365 exporter
// - OutputLoggingMiddleware emits output_messages spans
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
foreach (var sd in builder.Services) { var name = sd.ServiceType?.FullName ?? ""; if (name.Contains("TokenCache") || name.Contains("ExporterToken") || name.Contains("AgenticToken")) Console.WriteLine($"[DI-DEBUG] {name} -> {sd.ImplementationType?.Name ?? sd.Lifetime.ToString()}"); }

// Enable AspNet authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Message endpoint
app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await adapter.ProcessAsync(request, response, agent, cancellationToken);
});

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => "Agent 365 S2S Observability Sample Agent");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();

    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3985";
    app.Urls.Add($"http://localhost:{port}");
}
else
{
    app.MapControllers();
}

app.Run();
