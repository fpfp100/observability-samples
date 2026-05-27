// Non-agentic token acquisition sample — mirrors Node.js test-agents/agentic-ai/index1.ts.
// TOKEN_MODE=obo: OBO exchange → export to /observability
// TOKEN_MODE=s2s (default): client_credentials → export to /observabilityService

using NonAgenticSample;
using NonAgenticSample.Agents;
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

var tokenMode = Environment.GetEnvironmentVariable("TOKEN_MODE") ?? "s2s";
var useS2S = tokenMode.Equals("s2s", StringComparison.OrdinalIgnoreCase);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .Clear()
        .AddService(
            serviceName: "NonAgentic.Sample",
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.namespace"] = "Microsoft.Agents"
        }))
    .UseMicrosoftOpenTelemetry(o =>
    {
        o.Exporters = ExportTarget.Agent365 | ExportTarget.Console;

        // Custom TokenResolver reads from cache populated by agent handler (OBO or S2S)
        o.Agent365.Exporter.TokenResolver = (agentId, tenantId) =>
        {
            var token = TokenCache.Get(agentId, tenantId);
            Console.WriteLine($"[TokenResolver] agentId={agentId}, tenantId={tenantId}, hit={token != null}, len={token?.Length ?? 0}");
            return Task.FromResult(token ?? string.Empty);
        };

        // OBO → /observability, S2S → /observabilityService
        o.Agent365.Exporter.UseS2SEndpoint = useS2S;
    })
    .WithMetrics(metrics => metrics.AddView("*", MetricStreamConfiguration.Drop));

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddHttpClient();
builder.AddAgentApplicationOptions();
builder.AddAgent<EchoAgent>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();

builder.Services.AddControllers();
builder.Services.AddAgentAspNetAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAgentHttpAdapter, CloudAdapter>();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/messages", async (HttpRequest request, HttpResponse response, IAgentHttpAdapter adapter, IAgent agent, CancellationToken cancellationToken) =>
{
    await adapter.ProcessAsync(request, response, agent, cancellationToken);
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Playground")
{
    app.MapGet("/", () => $"Non-Agentic Token Acquisition Sample (mode={tokenMode})");
    app.UseDeveloperExceptionPage();
    app.MapControllers().AllowAnonymous();

    var port = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "3986";
    app.Urls.Add($"http://localhost:{port}");
}
else
{
    app.MapControllers();
}

app.Run();
