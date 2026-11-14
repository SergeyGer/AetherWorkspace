using Aether.Broker.Launching;
using Aether.Broker.Sessions;

namespace Aether.Broker.Health;

public enum AppHealthState
{
    Unknown = 0,
    Healthy = 1,
    Unresponsive = 2,
    Exited = 3,
    Restarting = 4,
}

public sealed record AppHealthReport(string AppId, string InstanceId, AppHealthState State, int MissedHeartbeats);

/// <summary>
/// Lazy health evaluation: process exit detection plus missed-heartbeat rules.
/// A crashed sample app never terminates the shell or the broker; the state is
/// reported deterministically and restart stays bounded (see AppLauncher).
/// </summary>
public sealed class HealthMonitor
{
    private readonly BrokerOptions _options;
    private readonly SessionManager _sessions;
    private readonly AppLauncher _launcher;

    public HealthMonitor(BrokerOptions options, SessionManager sessions, AppLauncher launcher)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public IReadOnlyList<AppHealthReport> Evaluate()
    {
        var now = _options.TimeProvider.GetUtcNow();
        var launched = _launcher.Snapshot()
            .GroupBy(app => app.InstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var reports = new List<AppHealthReport>();
        foreach (var session in _sessions.SnapshotSessions())
        {
            if (launched.TryGetValue(session.InstanceId, out var process) && process.HasExited)
            {
                reports.Add(new AppHealthReport(session.AppId, session.InstanceId, AppHealthState.Exited, 0));
                continue;
            }

            var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
            var elapsed = now - session.LastHeartbeatUtc;
            var missed = Math.Max(0, (int)(elapsed / interval) - 1);
            var state = missed >= _options.MissedHeartbeatsBeforeUnresponsive
                ? AppHealthState.Unresponsive
                : AppHealthState.Healthy;
            reports.Add(new AppHealthReport(session.AppId, session.InstanceId, state, missed));
        }

        foreach (var app in launched.Values)
        {
            if (_sessions.SnapshotSessions().Any(session => string.Equals(session.InstanceId, app.InstanceId, StringComparison.Ordinal)))
            {
                continue;
            }

            reports.Add(new AppHealthReport(
                app.AppId,
                app.InstanceId,
                app.HasExited ? AppHealthState.Exited : AppHealthState.Unknown,
                0));
        }

        return reports
            .OrderBy(report => report.AppId, StringComparer.Ordinal)
            .ThenBy(report => report.InstanceId, StringComparer.Ordinal)
            .ToArray();
    }
}
