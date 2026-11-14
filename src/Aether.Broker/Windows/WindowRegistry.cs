using Aether.Broker.Sessions;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;

namespace Aether.Broker.Windows;

/// <summary>
/// Tracks top-level windows reported by registered apps. Only windows owned by
/// an authenticated registered app are recorded; arbitrary third-party HWNDs are
/// never accepted.
/// </summary>
public sealed class WindowRegistry
{
    private readonly object _gate = new();
    private readonly BrokerOptions _options;
    private readonly Dictionary<(string AppId, string InstanceId, string WindowId), WindowState> _windows = new();

    public WindowRegistry(BrokerOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _windows.Count;
            }
        }
    }

    public bool Report(BrokerSession session, WindowState state, out string? denialCode, out string? denialMessage)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);
        denialCode = null;
        denialMessage = null;

        if (!string.Equals(session.AppId, state.AppId, StringComparison.Ordinal) ||
            !string.Equals(session.InstanceId, state.InstanceId, StringComparison.Ordinal))
        {
            denialCode = AetherErrorCodes.AuthIdentityMismatch;
            denialMessage = "A window may only be reported for the authenticated app instance.";
            return false;
        }

        if (!IsValidWindowId(state.WindowId) ||
            state.Title.Length > _options.MaxTitleLength ||
            !state.Bounds.IsSane() ||
            state.MonitorId.Length > 64)
        {
            denialCode = AetherErrorCodes.PlanActionFieldsInvalid;
            denialMessage = "The reported window description is invalid or out of bounds.";
            return false;
        }

        lock (_gate)
        {
            if (_windows.Count >= _options.MaxWindows && !_windows.ContainsKey((state.AppId, state.InstanceId, state.WindowId)))
            {
                denialCode = AetherErrorCodes.BrokerQueueOverflow;
                denialMessage = "The window registry is full.";
                return false;
            }

            _windows[(state.AppId, state.InstanceId, state.WindowId)] = state;
        }

        return true;
    }

    public bool HasWindowForInstance(string instanceId)
    {
        lock (_gate)
        {
            return _windows.Keys.Any(key => string.Equals(key.InstanceId, instanceId, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<WindowState> ListVisible()
    {
        lock (_gate)
        {
            return _windows.Values
                .Where(state => state.Visible)
                .OrderBy(state => state.AppId, StringComparer.Ordinal)
                .ThenBy(state => state.InstanceId, StringComparer.Ordinal)
                .ThenBy(state => state.WindowId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public void RemoveInstance(string instanceId)
    {
        lock (_gate)
        {
            foreach (var key in _windows.Keys.Where(key => string.Equals(key.InstanceId, instanceId, StringComparison.Ordinal)).ToArray())
            {
                _windows.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _windows.Clear();
        }
    }

    private static bool IsValidWindowId(string windowId) =>
        !string.IsNullOrWhiteSpace(windowId) &&
        windowId.Length <= 64 &&
        windowId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
