// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// In-memory S2S observability token cache, matching the Node.js/Python pattern.
// The agent acquires the token in its message handler and caches it here.
// The distro's TokenResolver reads from this cache at export time.

using System.Collections.Concurrent;

namespace S2SSampleAgent;

public static class S2STokenCache
{
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    public static void Set(string agentId, string tenantId, string token)
        => _cache[$"{agentId}:{tenantId}"] = token;

    public static string? Get(string agentId, string tenantId)
        => _cache.TryGetValue($"{agentId}:{tenantId}", out var token) ? token : null;
}
