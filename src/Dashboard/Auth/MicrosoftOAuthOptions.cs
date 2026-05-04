namespace AzureFinOps.Dashboard.Auth;

/// <summary>
/// Strongly-typed Microsoft Entra ID multi-tenant OAuth configuration.
/// Bound from <c>Microsoft:*</c> config keys.
/// </summary>
public sealed class MicrosoftOAuthOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string TenantId { get; init; } = "common";
    public string HomeTenantId { get; init; } = "common";

    /// <summary>
    /// When true, forces <c>prompt=login</c> on every authorize redirect so cached
    /// browser accounts from other tenants are bypassed. Useful for local demos
    /// against a single-tenant app reg. Default false (keeps the friendlier
    /// <c>select_account</c> behavior for production multi-tenant use).
    /// </summary>
    public bool ForceLoginPrompt { get; init; }

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId);

    /// <summary>
    /// OAuth tier → resource-specific scopes. Single source of truth for both the
    /// authorize redirect and the token exchange.
    /// </summary>
    public static string[] GetScopesForTier(string tier) => tier switch
    {
        "licenses" => ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/Organization.Read.All", "https://graph.microsoft.com/Reports.Read.All"],
        "chargeback" => ["https://graph.microsoft.com/User.Read", "https://graph.microsoft.com/User.Read.All", "https://graph.microsoft.com/Group.Read.All"],
        "loganalytics" => ["https://api.loganalytics.io/Data.Read"],
        "storage" => ["https://storage.azure.com/user_impersonation"],
        _ => ["https://management.azure.com/user_impersonation"]
    };

    /// <summary>
    /// Normalize host for OAuth callbacks — strip "www." so callbacks always match
    /// the registered redirect URIs (e.g. azure-finops-agent.com, not www.azure-finops-agent.com).
    /// </summary>
    public static string NormalizeCallbackHost(HttpContext ctx)
    {
        var host = ctx.Request.Host.ToString();
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];
        return $"{ctx.Request.Scheme}://{host}";
    }
}
