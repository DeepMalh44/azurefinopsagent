using System.Collections.Concurrent;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Per-request token storage using AsyncLocal with per-user ConcurrentDictionary fallback.
/// AsyncLocal is the primary mechanism — tokens flow with the async context.
/// The per-user fallback (keyed by userId) handles cases where the Copilot SDK
/// invokes tool functions on a thread pool thread where AsyncLocal doesn't flow.
/// This ensures strict user isolation — no cross-tenant token leakage.
/// </summary>
public static class TokenContext
{
    private static readonly AsyncLocal<string?> _azureToken = new();
    private static readonly AsyncLocal<string?> _graphToken = new();
    private static readonly AsyncLocal<string?> _logAnalyticsToken = new();
    private static readonly AsyncLocal<long> _userId = new();

    // Per-user token fallback — keyed by userId, strictly isolated between users.
    private static readonly ConcurrentDictionary<long, string> _userAzureTokens = new();
    private static readonly ConcurrentDictionary<long, string> _userGraphTokens = new();
    private static readonly ConcurrentDictionary<long, string> _userLogAnalyticsTokens = new();

    /// <summary>Set the current user ID for per-user token fallback. Must be called before setting tokens.</summary>
    public static long CurrentUserId
    {
        get => _userId.Value;
        set => _userId.Value = value;
    }

    /// <summary>Azure ARM API token (management.azure.com)</summary>
    public static string? AzureToken
    {
        get => _azureToken.Value ?? (_userId.Value != 0 && _userAzureTokens.TryGetValue(_userId.Value, out var t) ? t : null);
        set
        {
            _azureToken.Value = value;
            if (_userId.Value != 0 && value is not null)
                _userAzureTokens[_userId.Value] = value;
        }
    }

    /// <summary>Microsoft Graph API token (graph.microsoft.com)</summary>
    public static string? GraphToken
    {
        get => _graphToken.Value ?? (_userId.Value != 0 && _userGraphTokens.TryGetValue(_userId.Value, out var t) ? t : null);
        set
        {
            _graphToken.Value = value;
            if (_userId.Value != 0 && value is not null)
                _userGraphTokens[_userId.Value] = value;
        }
    }

    /// <summary>Log Analytics / App Insights API token (api.loganalytics.io / api.applicationinsights.io)</summary>
    public static string? LogAnalyticsToken
    {
        get => _logAnalyticsToken.Value ?? (_userId.Value != 0 && _userLogAnalyticsTokens.TryGetValue(_userId.Value, out var t) ? t : null);
        set
        {
            _logAnalyticsToken.Value = value;
            if (_userId.Value != 0 && value is not null)
                _userLogAnalyticsTokens[_userId.Value] = value;
        }
    }

    /// <summary>Clear all cached tokens for a specific user (call on logout/disconnect).</summary>
    public static void ClearUser(long userId)
    {
        _userAzureTokens.TryRemove(userId, out _);
        _userGraphTokens.TryRemove(userId, out _);
        _userLogAnalyticsTokens.TryRemove(userId, out _);
    }
}
