using System.Net.Http.Json;
using System.Text.Json;

const string AgentUrl = "http://localhost:3978/api/messages";
const string ListenUrl = "http://localhost:56150";
const string ConnectorBase = "/_connector";

// Loop knobs — override via env var if you want something different without editing code.
int loopIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("LOOP_INTERVAL_SECONDS"), out var iv) && iv > 0 ? iv : 10;
int loopCount = int.TryParse(Environment.GetEnvironmentVariable("LOOP_COUNT"), out var lc) && lc >= 0 ? lc : 0; // 0 = infinite
string[] loopMessages = (Environment.GetEnvironmentVariable("LOOP_MESSAGES") ??
    "what can you do|summarize my inbox|list my unread emails|draft an email to my team|what time is it")
    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

var app = builder.Build();
app.Urls.Add(ListenUrl);

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
    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        int i = 0;
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var text = loopMessages[i % loopMessages.Length];
            await SendActivity(text);
            i++;
            if (loopCount > 0 && i >= loopCount) break;
            try { await Task.Delay(TimeSpan.FromSeconds(loopIntervalSeconds), app.Lifetime.ApplicationStopping); }
            catch (TaskCanceledException) { break; }
        }
    });
});

app.Run();

static async Task SendActivity(string text)
{
    var payload = new
    {
        type = "message",
        text = text,
        id = Guid.NewGuid().ToString(),
        channelId = "msteams",
        from = new
        {
            id = "user-id-0",
            name = "Alex Wilber",
            aadObjectId = "a92962f3-9ed4-4bcd-9ae0-ad0002b6ca76"
        },
        timestamp = "2025-10-03T16:33:10.550Z",
        localTimestamp = "2025-10-03T09:33:10.550-07:00",
        localTimezone = "America/Los_Angeles",
        serviceUrl = $"{ListenUrl}{ConnectorBase}",
        conversation = new
        {
            conversationType = "personal",
            tenantId = "badf1f56-284d-4dc5-ac59-0dd53900e743",
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
        channelData = new { tenant = new { id = "00000000-0000-0000-0000-0000000000001" } }
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
