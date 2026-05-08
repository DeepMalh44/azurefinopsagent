namespace AzureFinOps.Dashboard.AI;

/// <summary>
/// Result of a <see cref="ChatModerator.EvaluateAsync"/> call.
/// </summary>
/// <param name="TransientFailure">
/// True when the verdict is Allow-by-fail-open due to a transient error (429, timeout, network).
/// Callers can use this to show a brief inline notice to the user.
/// </param>
/// <param name="TransientReason">
/// Machine-readable reason code when <see cref="TransientFailure"/> is true.
/// Values: "rate_limit", "timeout", "network", "http_503".
/// </param>
public sealed record ModerationVerdict(
    bool IsAllowed,
    string? RuleViolated,
    string? UserMessage,
    bool TransientFailure = false,
    string? TransientReason = null)
{
    public static ModerationVerdict Allow() => new(true, null, null);

    /// <summary>
    /// Fail-open verdict that signals a transient infrastructure problem to the caller.
    /// </summary>
    public static ModerationVerdict AllowWithTransient(string reason) =>
        new(true, null, null, TransientFailure: true, TransientReason: reason);

    public static ModerationVerdict Block(string rule, string message) => new(false, rule, message);
}
