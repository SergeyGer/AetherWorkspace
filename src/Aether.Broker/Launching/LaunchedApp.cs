using System.Diagnostics;

namespace Aether.Broker.Launching;

/// <summary>Tracked sample-app process launched by the broker.</summary>
public sealed record LaunchedApp(
    string AppId,
    string InstanceId,
    Process Process,
    string ExecutablePath,
    DateTimeOffset LaunchedAtUtc,
    DateTimeOffset RegistrationExpiresAtUtc)
{
    public int ProcessId => Process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }
}

public sealed record LaunchOutcome(
    bool Success,
    LaunchedApp? App,
    bool ReusedExistingInstance,
    string? DenialCode,
    string? DenialMessage)
{
    public static LaunchOutcome Ok(LaunchedApp app, bool reusedExistingInstance) => new(true, app, reusedExistingInstance, null, null);

    public static LaunchOutcome Deny(string code, string message) => new(false, null, false, code, message);
}

public sealed record StopOutcome(bool Stopped, string? DenialCode, string? DenialMessage)
{
    public static StopOutcome Ok() => new(true, null, null);

    public static StopOutcome Deny(string code, string message) => new(false, code, message);
}
