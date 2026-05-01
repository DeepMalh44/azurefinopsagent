using AzureFinOps.Dashboard.Observability;

namespace AzureFinOps.Dashboard.Auth;

/// <summary>
/// Background service that evicts per-user state (CopilotSession, UserTokens, tool list)
/// when the user has been inactive for a configurable period. Without this, the in-memory
/// dictionaries grow unbounded as anonymous visitors accumulate, eventually OOM'ing the
/// container. We track <see cref="LastSeenUtc"/> on every chat request and sweep on a timer.
/// </summary>
public sealed class UserStateJanitor : BackgroundService
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTimeOffset> LastSeenUtc = new();

    private static readonly TimeSpan IdleThreshold = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    private readonly AiTelemetry _telemetry;
    private readonly ILogger<UserStateJanitor> _logger;

    public UserStateJanitor(AiTelemetry telemetry, ILogger<UserStateJanitor> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Sweep(); }
            catch (Exception ex) { _logger.LogWarning(ex, "UserStateJanitor sweep failed"); }
            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleThreshold;
        var evicted = 0;
        foreach (var (userId, lastSeen) in LastSeenUtc)
        {
            if (lastSeen >= cutoff) continue;

            LastSeenUtc.TryRemove(userId, out _);
            _telemetry.UserTokens.TryRemove(userId, out _);
            _telemetry.UserTools.TryRemove(userId, out _);
            if (_telemetry.UserSessions.TryRemove(userId, out var session))
            {
                _telemetry.ActiveSessions.Add(-1);
                try { await session.DisposeAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose Copilot session for user {UserId}", userId); }
            }
            evicted++;
        }

        if (evicted > 0)
            _logger.LogInformation("UserStateJanitor evicted {Count} idle user(s); active={Active}",
                evicted, LastSeenUtc.Count);
    }
}
