using System.Text.Json;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;

namespace Aether.Contracts.Models.Workspaces;

/// <summary>Rectangle in virtual-desktop pixels.</summary>
public sealed record WorkspaceRect(int X, int Y, int Width, int Height)
{
    public bool IsSane() =>
        Width is > 0 and <= 32768 &&
        Height is > 0 and <= 32768 &&
        X is >= -32768 and <= 32768 &&
        Y is >= -32768 and <= 32768;
}

/// <summary>One physical/virtual monitor captured in a durable workspace layout.</summary>
public sealed record WorkspaceMonitor(
    string DeviceName,
    bool Primary,
    WorkspaceRect Bounds,
    WorkspaceRect WorkArea,
    double DpiScale);

/// <summary>Saved window placement for one registered application.</summary>
public sealed record WorkspaceBinding(
    string AppId,
    string MonitorDeviceName,
    WorkspaceRect Bounds,
    bool Visible = true);

/// <summary>
/// Durable, versioned multi-monitor workspace layout. Persisted locally by the
/// broker; unknown members are rejected by the strict JSON options.
/// </summary>
public sealed record WorkspaceLayout(
    string SchemaVersion,
    string LayoutId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<WorkspaceMonitor> Monitors,
    IReadOnlyList<WorkspaceBinding> Bindings,
    string? Description = null);

/// <summary>One validation issue for a workspace layout document.</summary>
public sealed record WorkspaceLayoutIssue(string Code, string Message);

/// <summary>Result of parsing a workspace layout document.</summary>
public sealed record WorkspaceLayoutParseResult(
    WorkspaceLayout? Layout,
    IReadOnlyList<WorkspaceLayoutIssue> Issues)
{
    public bool Success => Layout is not null && Issues.Count == 0;
}

/// <summary>Strict validation rules shared by persistence, broker RPC and tests.</summary>
public static class WorkspaceLayoutValidation
{
    public const string CurrentSchemaVersion = "1.0";
    public const int MaxMonitors = 16;
    public const int MaxBindings = 64;
    public const int MaxLayoutIdLength = 64;
    public const int MaxAppIdLength = 128;
    public const int MaxDeviceNameLength = 128;
    public const int MaxDescriptionLength = 512;
    public const double MinimumDpiScale = 0.5;
    public const double MaximumDpiScale = 4.0;

    public static IReadOnlyList<WorkspaceLayoutIssue> Validate(WorkspaceLayout? layout)
    {
        var issues = new List<WorkspaceLayoutIssue>();
        if (layout is null)
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, "The workspace layout document is missing."));
            return issues;
        }

        if (!string.Equals(layout.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Unsupported workspace layout schema version '{layout.SchemaVersion}'."));
        }

        if (string.IsNullOrWhiteSpace(layout.LayoutId) || layout.LayoutId.Length > MaxLayoutIdLength)
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"A layout id of 1..{MaxLayoutIdLength} characters is required."));
        }

        if (layout.Description is { Length: > MaxDescriptionLength })
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Description exceeds {MaxDescriptionLength} characters."));
        }

        var monitors = layout.Monitors ?? [];
        if (monitors.Count is < 1 or > MaxMonitors)
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"A workspace layout must contain 1..{MaxMonitors} monitors."));
        }

        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaries = 0;
        foreach (var monitor in monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.DeviceName) || monitor.DeviceName.Length > MaxDeviceNameLength)
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, "Every monitor needs a non-empty device name."));
            }
            else if (!devices.Add(monitor.DeviceName))
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Duplicate monitor device name '{monitor.DeviceName}'."));
            }

            if (monitor.Primary)
            {
                primaries++;
            }

            if (!monitor.Bounds.IsSane() || !monitor.WorkArea.IsSane())
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Monitor '{monitor.DeviceName}' has an insane rectangle."));
            }

            if (monitor.DpiScale is < MinimumDpiScale or > MaximumDpiScale)
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Monitor '{monitor.DeviceName}' DPI scale must be {MinimumDpiScale}..{MaximumDpiScale}."));
            }
        }

        if (primaries != 1)
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, "Exactly one monitor must be marked primary."));
        }

        var bindings = layout.Bindings ?? [];
        if (bindings.Count > MaxBindings)
        {
            issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"A workspace layout supports at most {MaxBindings} bindings."));
        }

        var apps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.AppId) || binding.AppId.Length > MaxAppIdLength)
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, "Every binding needs a non-empty app id."));
                continue;
            }

            if (!apps.Add(binding.AppId))
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Duplicate binding for app '{binding.AppId}'."));
            }

            if (string.IsNullOrWhiteSpace(binding.MonitorDeviceName) || binding.MonitorDeviceName.Length > MaxDeviceNameLength)
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Binding '{binding.AppId}' has an invalid monitor device name."));
            }
            else if (!devices.Contains(binding.MonitorDeviceName))
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Binding '{binding.AppId}' references unknown monitor '{binding.MonitorDeviceName}'."));
            }

            if (!binding.Bounds.IsSane())
            {
                issues.Add(new(AetherErrorCodes.WorkspaceLayoutInvalid, $"Binding '{binding.AppId}' has an insane rectangle."));
            }
        }

        return issues;
    }
}

/// <summary>Strict workspace-layout parser (unknown JSON members are rejected).</summary>
public static class WorkspaceLayoutParser
{
    public static WorkspaceLayoutParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(null, [new(AetherErrorCodes.WorkspaceLayoutInvalid, "The workspace layout document is empty.")]);
        }

        try
        {
            var layout = AetherJson.DeserializeStrict<WorkspaceLayout>(json);
            var issues = WorkspaceLayoutValidation.Validate(layout);
            return new(layout, issues);
        }
        catch (AetherContractException exception)
        {
            return new(null, [new(AetherErrorCodes.WorkspaceLayoutInvalid, exception.Message)]);
        }
        catch (JsonException exception)
        {
            return new(null, [new(AetherErrorCodes.WorkspaceLayoutInvalid, exception.Message)]);
        }
    }
}
