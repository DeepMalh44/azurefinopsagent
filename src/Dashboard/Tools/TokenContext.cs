namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Per-user mutable token holder. One instance per user, stored in a ConcurrentDictionary
/// keyed by userId. Passed to tool constructors via closure — tools always read the latest
/// tokens via direct reference. Volatile fields ensure cross-thread visibility.
/// </summary>
public class UserTokens
{
    private volatile string? _azureToken;
    private volatile string? _graphToken;
    private volatile string? _logAnalyticsToken;

    /// <summary>Azure ARM API token (management.azure.com)</summary>
    public string? AzureToken { get => _azureToken; set => _azureToken = value; }

    /// <summary>Microsoft Graph API token (graph.microsoft.com)</summary>
    public string? GraphToken { get => _graphToken; set => _graphToken = value; }

    /// <summary>Log Analytics / App Insights API token (api.loganalytics.io)</summary>
    public string? LogAnalyticsToken { get => _logAnalyticsToken; set => _logAnalyticsToken = value; }

    /// <summary>Lock for serializing token refresh operations to prevent double-refresh races.</summary>
    public SemaphoreSlim RefreshLock { get; } = new(1, 1);
}
