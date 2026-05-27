using System.Collections.Concurrent;

namespace NonAgenticSample;

/// <summary>
/// Shared in-memory token cache for the observability exporter.
/// Populated by either OBO or S2S flow in the agent handler.
/// </summary>
public static class TokenCache
{
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    public static void Set(string agentId, string tenantId, string token)
        => _cache[$"{agentId}:{tenantId}"] = token;

    public static string? Get(string agentId, string tenantId)
        => _cache.TryGetValue($"{agentId}:{tenantId}", out var token) ? token : null;
}
