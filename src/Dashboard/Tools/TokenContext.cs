namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Per-request token storage using AsyncLocal with volatile static fallback.
/// AsyncLocal is the primary mechanism for concurrent user isolation.
/// The volatile fallback handles cases where the Copilot SDK executes tool
/// functions on a different async context (e.g., internal thread pool threads)
/// where AsyncLocal values don't flow from the chat handler.
/// </summary>
public static class TokenContext
{
    private static readonly AsyncLocal<string?> _azureToken = new();
    private static readonly AsyncLocal<string?> _graphToken = new();
    private static readonly AsyncLocal<string?> _logAnalyticsToken = new();

    // Volatile fallback for when AsyncLocal doesn't flow into SDK tool execution.
    // Last-writer-wins — safe for single-user local dev; AsyncLocal handles concurrent production use.
    private static volatile string? _fallbackAzureToken;
    private static volatile string? _fallbackGraphToken;
    private static volatile string? _fallbackLogAnalyticsToken;

    /// <summary>Azure ARM API token (management.azure.com)</summary>
    public static string? AzureToken
    {
        get => _azureToken.Value ?? _fallbackAzureToken;
        set { _azureToken.Value = value; _fallbackAzureToken = value; }
    }

    /// <summary>Microsoft Graph API token (graph.microsoft.com)</summary>
    public static string? GraphToken
    {
        get => _graphToken.Value ?? _fallbackGraphToken;
        set { _graphToken.Value = value; _fallbackGraphToken = value; }
    }

    /// <summary>Log Analytics / App Insights API token (api.loganalytics.io / api.applicationinsights.io)</summary>
    public static string? LogAnalyticsToken
    {
        get => _logAnalyticsToken.Value ?? _fallbackLogAnalyticsToken;
        set { _logAnalyticsToken.Value = value; _fallbackLogAnalyticsToken = value; }
    }
}
