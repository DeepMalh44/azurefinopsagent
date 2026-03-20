namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Per-user mutable token holder. One instance per user, stored in a ConcurrentDictionary
/// keyed by userId. Passed to tool constructors via closure — tools always read the latest
/// tokens via direct reference. No static state, no AsyncLocal, no thread-safety issues.
/// </summary>
public class UserTokens
{
    /// <summary>Azure ARM API token (management.azure.com)</summary>
    public string? AzureToken { get; set; }

    /// <summary>Microsoft Graph API token (graph.microsoft.com)</summary>
    public string? GraphToken { get; set; }

    /// <summary>Log Analytics / App Insights API token (api.loganalytics.io)</summary>
    public string? LogAnalyticsToken { get; set; }
}
