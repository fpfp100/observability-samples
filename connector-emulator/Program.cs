using System.Net.Http.Json;
using System.Text.Json;

string AgentUrl = Environment.GetEnvironmentVariable("AGENT_URL") ?? "http://localhost:3978/api/messages";
const string ConnectorBase = "/_connector";

// Use port 0 so the OS assigns an available port automatically.
string ListenUrl = Environment.GetEnvironmentVariable("EMULATOR_LISTEN_URL") ?? "http://127.0.0.1:0";

// Loop knobs — override via env var if you want something different without editing code.
int loopIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("LOOP_INTERVAL_SECONDS"), out var iv) && iv > 0 ? iv : 10;
int loopCount = int.TryParse(Environment.GetEnvironmentVariable("LOOP_COUNT"), out var lc) && lc >= 0 ? lc : 1; // default 1; 0 = infinite
string[] loopMessages = (Environment.GetEnvironmentVariable("LOOP_MESSAGES") ??
    "what is the current date and time|summarize my inbox|list my unread emails|draft an email to my team|what time is it")
    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

var app = builder.Build();
app.Urls.Add(ListenUrl);

// Resolved after the server starts — holds the actual base URL (with real port).
string resolvedListenUrl = ListenUrl;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

async Task<IResult> LogActivity(HttpRequest req, string? conversationId = null, string? activityId = null)
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();

    Console.WriteLine();
    Console.WriteLine($"━━━ {req.Method} {req.Path} ━━━");
    if (conversationId is not null) Console.WriteLine($"  conversationId: {conversationId}");
    if (activityId is not null) Console.WriteLine($"  replyTo: {activityId}");
    var auth = req.Headers.Authorization.ToString();
    if (!string.IsNullOrEmpty(auth)) Console.WriteLine($"  auth: {auth[..Math.Min(60, auth.Length)]}…");
    try
    {
        using var doc = JsonDocument.Parse(body);
        Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, jsonOptions));
    }
    catch
    {
        Console.WriteLine(body);
    }

    return Results.Ok(new { id = Guid.NewGuid().ToString() });
}

app.MapPost($"{ConnectorBase}/v3/conversations", async (HttpRequest req) =>
{
    await LogActivity(req);
    return Results.Ok(new { id = Guid.NewGuid().ToString() });
});

app.MapPost($"{ConnectorBase}/v3/conversations/{{conversationId}}/activities",
    (HttpRequest req, string conversationId) => LogActivity(req, conversationId));

app.MapPost($"{ConnectorBase}/v3/conversations/{{conversationId}}/activities/{{activityId}}",
    (HttpRequest req, string conversationId, string activityId) => LogActivity(req, conversationId, activityId));

app.MapPut($"{ConnectorBase}/v3/conversations/{{conversationId}}/activities/{{activityId}}",
    (HttpRequest req, string conversationId, string activityId) => LogActivity(req, conversationId, activityId));

app.MapDelete($"{ConnectorBase}/v3/conversations/{{conversationId}}/activities/{{activityId}}",
    (string conversationId, string activityId) =>
    {
        Console.WriteLine($"━━━ DELETE /v3/conversations/{conversationId}/activities/{activityId} ━━━");
        return Results.Ok();
    });

app.MapMethods("/{**catch}", new[] { "GET", "POST", "PUT", "DELETE" },
    async (HttpRequest req) =>
    {
        Console.WriteLine($"━━━ UNMATCHED {req.Method} {req.Path}{req.QueryString} ━━━");
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(body)) Console.WriteLine(body);
        return Results.Ok();
    });

app.Lifetime.ApplicationStarted.Register(() =>
{
    // Resolve the actual listen URL (important when using port 0).
    var addr = app.Urls.FirstOrDefault() ?? ListenUrl;
    var serverFeature = ((IApplicationBuilder)app).ServerFeatures.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
    if (serverFeature?.Addresses.Count > 0)
        addr = serverFeature.Addresses.First();
    resolvedListenUrl = addr;
    Console.WriteLine($"  Callback URL: {resolvedListenUrl}{ConnectorBase}");

    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        int i = 0;
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var text = loopMessages[i % loopMessages.Length];
            await SendActivity(text, resolvedListenUrl);
            i++;
            if (loopCount > 0 && i >= loopCount)
            {
                // Give the agent a moment to send callbacks, then shut down.
                await Task.Delay(TimeSpan.FromSeconds(5));
                app.Lifetime.StopApplication();
                break;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(loopIntervalSeconds), app.Lifetime.ApplicationStopping); }
            catch (TaskCanceledException) { break; }
        }
    });
});

app.Run();

async Task SendActivity(string text, string listenUrl)
{
    var tenantId = Environment.GetEnvironmentVariable("AGENT_TENANT_ID") ?? "badf1f56-284d-4dc5-ac59-0dd53900e743";

    var payload = new
    {
        type = "message",
        text = text,
        id = Guid.NewGuid().ToString(),
        channelId = "emulator",
        from = new
        {
            id = "user-id-0",
            name = "Alex Wilber",
            aadObjectId = "a92962f3-9ed4-4bcd-9ae0-ad0002b6ca76"
        },
        timestamp = "2025-10-03T16:33:10.550Z",
        localTimestamp = "2025-10-03T09:33:10.550-07:00",
        localTimezone = "America/Los_Angeles",
        serviceUrl = $"{listenUrl}{ConnectorBase}",
        conversation = new
        {
            conversationType = "personal",
            tenantId = tenantId,
            id = "d6134d32-d455-49a0-9988-d8bd542ca4b0"
        },
        recipient = new
        {
            id = "nikhilcagent0416@a365preview070.onmicrosoft.com",
            name = "nikhilcagent0416 Agent User",
            tenantId = "badf1f56-284d-4dc5-ac59-0dd53900e743",
            agenticUserId = "74ae7173-86f8-41b0-a237-c4c4822fb9bc",
            agenticAppId = "d6d31512-379f-4994-b901-c098dfd9293f",
            role = "agenticUser"
        },
        textFormat = "plain",
        locale = "en-US",
        entities = new object[]
        {
            new { type = "clientInfo", locale = "en-US", country = "US", platform = "Web", timezone = "America/Los_Angeles" }
        },
        channelData = new { tenant = new { id = tenantId } }
    };

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    Console.WriteLine();
    Console.WriteLine($"━━━ POST {AgentUrl} — \"{text}\" ━━━");
    try
    {
        var response = await client.PostAsJsonAsync(AgentUrl, payload);
        Console.WriteLine($"  ← {(int)response.StatusCode} {response.ReasonPhrase}");
        var respBody = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(respBody))
        {
            Console.WriteLine($"  body: {respBody}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ {ex.GetType().Name}: {ex.Message}");
    }
}
