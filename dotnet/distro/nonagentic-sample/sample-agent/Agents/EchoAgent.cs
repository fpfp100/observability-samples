// Non-agentic token acquisition sample — mirrors Node.js test-agents/agentic-ai/index1.ts.
// TOKEN_MODE=s2s (default): connection.getAccessToken → export to /observabilityService
// TOKEN_MODE=obo: authorization.exchangeToken → export to /observability

using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Messages;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Authentication;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NonAgenticSample.Agents;

public class EchoAgent : AgentApplication
{
    private readonly IConnections _connections;
    private readonly IConfiguration _configuration;

    public EchoAgent(AgentApplicationOptions options, IConnections connections, IConfiguration configuration) : base(options)
    {
        _connections = connections;
        _configuration = configuration;

        // Mirrors JS: this.onActivity('message', this.agentic, ['oboConnectionProfile'])
        // autoSignInHandlers only for OBO mode (requires dev tunnel) — emulator can't do agentic auth.
        var tokenMode = Environment.GetEnvironmentVariable("TOKEN_MODE") ?? "s2s";
        if (tokenMode.Equals("obo", StringComparison.OrdinalIgnoreCase))
        {
            OnActivity(ActivityTypes.Message, OnMessageAsync, autoSignInHandlers: new[] { "oboConnectionProfile" });
        }
        else
        {
            OnActivity(ActivityTypes.Message, OnMessageAsync);
        }
        OnActivity(ActivityTypes.Message, OnEchoAsync, rank: RouteRank.Last);
    }

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        var userText = turnContext.Activity?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(userText)) return;

        // Force agentId to ServiceConnection ClientId (non-agentic — no agenticAppId)
        var agentId = _configuration["Connections:ServiceConnection:Settings:ClientId"] ?? "unknown";
        var tenantId = _configuration["TokenValidation:TenantId"]
            ?? turnContext.Activity?.Conversation?.TenantId
            ?? "unknown";

        // Set baggage so the Agent365 exporter knows which agent/tenant to resolve tokens for
        using var baggageScope = new BaggageBuilder()
            .TenantId(tenantId)
            .AgentId(agentId)
            .Build();

        // Acquire token based on TOKEN_MODE
        var tokenMode = Environment.GetEnvironmentVariable("TOKEN_MODE") ?? "s2s";
        string? token = null;

        if (tokenMode.Equals("obo", StringComparison.OrdinalIgnoreCase))
        {
            // OBO: single call — mirrors JS: this.authorization.getToken(ctx, 'oboConnectionProfile')
            // The Azure Bot OAuth connection is configured with the observability scope,
            // so GetTurnTokenAsync returns a token already scoped to the right audience.
            // No ExchangeTurnTokenAsync needed.
            try
            {
                token = await UserAuthorization.GetTurnTokenAsync(turnContext, "oboConnectionProfile", cancellationToken);
                if (!string.IsNullOrEmpty(token))
                {
                    var decoded = DecodeJwt(token);
                    Console.WriteLine($"[OBO] Token acquired: aud={decoded.GetValueOrDefault("aud", "n/a")}, name={decoded.GetValueOrDefault("name", "n/a")}, scp={decoded.GetValueOrDefault("scp", "n/a")}, len={token.Length}");
                    TokenCache.Set(agentId, tenantId, token);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OBO] Token acquisition failed: {ex.Message}");
            }
        }
        else
        {
            // S2S: connection.getAccessToken('api://9b975845-.../.default')
            token = await _testS2S(agentId, tenantId);
        }

        var tokenDecoded = DecodeJwt(token);

        // Step 3: Create InvokeAgentScope — produces the span the exporter will send
        var agentDetails = new AgentDetails(
            agentId: agentId,
            agentName: "NonAgenticSample",
            agentDescription: "Non-agentic token acquisition + span export sample",
            tenantId: tenantId);

        var request = new Request(
            inputContent: new InputMessages(new[]
            {
                new ChatMessage(MessageRole.User, new IMessagePart[] { new TextPart(userText) })
            }),
            conversationId: turnContext.Activity?.Conversation?.Id,
            channel: turnContext.Activity?.ChannelId != null ? new Channel(turnContext.Activity.ChannelId) : null);

        using var invokeScope = InvokeAgentScope.Start(request, new InvokeAgentScopeDetails(), agentDetails);

        var responseText =
            $"You said: {userText}\n\n" +
            $"**({tokenMode})** token={token?.Length ?? 0} chars\n\n" +
            $"**appid**={tokenDecoded.GetValueOrDefault("appid", tokenDecoded.GetValueOrDefault("azp", "n/a"))}\n\n" +
            $"**tid**={tokenDecoded.GetValueOrDefault("tid", "n/a")}\n\n" +
            $"**aud**={tokenDecoded.GetValueOrDefault("aud", "n/a")}\n\n" +
            $"**agentId**={agentId}\n\n**tenantId**={tenantId}";

        invokeScope.RecordOutputMessages(new[] { responseText });
        Console.WriteLine(responseText);

        try
        {
            await turnContext.SendActivityAsync(MessageFactory.Text(responseText), cancellationToken);
        }
        catch (Exception ex)
        {
            // sendActivity may fail with emulator (outbound auth) — token + span still exported
            Console.WriteLine($"[sendActivity failed] {ex.Message}");
        }
    }

    /// <summary>
    /// Mirrors JS: connection.getAccessToken('api://9b975845-.../.default')
    /// </summary>
    private async Task<string?> _testS2S(string agentId, string tenantId)
    {
        try
        {
            var connection = _connections.GetConnection("ServiceConnection");
            var token = await connection.GetAccessTokenAsync(
                "https://login.microsoftonline.com",
                new List<string> { "api://9b975845-388f-4429-889e-eab1ef63949c/.default" });

            if (!string.IsNullOrEmpty(token))
            {
                TokenCache.Set(agentId, tenantId, token);
                Console.WriteLine($"[S2S] Token acquired and cached: agentId={agentId}, tenantId={tenantId}, len={token.Length}");
            }
            else
            {
                Console.WriteLine($"[S2S] Token was empty");
            }
            return token;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[S2S] Token acquisition failed: {ex.Message}");
            return null;
        }
    }

    private async Task OnEchoAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        await turnContext.SendActivityAsync($"You said: {turnContext.Activity.Text}", cancellationToken: cancellationToken);
    }

    private static Dictionary<string, string> DecodeJwt(string? token)
    {
        if (string.IsNullOrEmpty(token)) return new();
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);
        }
        catch { return new(); }
    }
}
