using System.Text.RegularExpressions;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Catalog;
using Aether.Execution.Results;

namespace Aether.Execution.Validation;

public sealed record ActionPlanValidationOptions
{
    public int MaxActions { get; init; } = PlanLimits.DefaultMaxActions;
}

/// <summary>
/// Deterministic plan validator. This is the only component allowed to decide
/// whether an untrusted plan proposal is structurally acceptable. It enforces
/// the action allowlist, bounded action counts, typed fields per action, known
/// apps/contexts and rejection of path/URL/shell-like content.
/// </summary>
public sealed partial class ActionPlanValidator
{
    private readonly ActionPlanValidationOptions _options;

    public ActionPlanValidator(ActionPlanValidationOptions? options = null)
    {
        _options = options ?? new ActionPlanValidationOptions();
        if (_options.MaxActions is < 1 or > PlanLimits.AbsoluteMaxActions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxActions must be between 1 and {PlanLimits.AbsoluteMaxActions}.");
        }
    }

    public int MaxActions => _options.MaxActions;

    public ActionPlanValidationResult Validate(ActionPlan? plan, PlanCatalog catalog) =>
        Validate(plan, _options, catalog);

    /// <summary>Validates against an explicit bounded action limit (defence in depth).</summary>
    public ActionPlanValidationResult Validate(ActionPlan? plan, ActionPlanValidationOptions options, PlanCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);

        if (options.MaxActions is < 1 or > PlanLimits.AbsoluteMaxActions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxActions must be between 1 and {PlanLimits.AbsoluteMaxActions}.");
        }

        if (plan is null)
        {
            return ActionPlanValidationResult.Invalid(
                new PlanValidationIssue(AetherErrorCodes.PlanInvalidJson, "Plan document is missing.", null));
        }

        var issues = new List<PlanValidationIssue>();

        if (!SchemaVersions.IsSupported(plan.SchemaVersion))
        {
            issues.Add(new PlanValidationIssue(
                AetherErrorCodes.PlanSchemaVersionUnsupported,
                "Unsupported plan schema version. This build accepts schema major version 1.",
                null));
        }

        if (!IsValidIdentifier(plan.PlanId, PlanLimits.MaxPlanIdLength))
        {
            issues.Add(new PlanValidationIssue(
                AetherErrorCodes.PlanIdInvalid,
                "Plan id is missing or contains unsupported characters.",
                null));
        }

        if (plan.Description is { Length: > PlanLimits.MaxDescriptionLength })
        {
            issues.Add(new PlanValidationIssue(
                AetherErrorCodes.PlanUnsafeContent,
                "Plan description is too long.",
                null));
        }

        var steps = plan.Steps;
        if (steps.Count == 0)
        {
            issues.Add(new PlanValidationIssue(AetherErrorCodes.PlanStepsEmpty, "Plan contains no steps.", null));
            return new ActionPlanValidationResult(false, issues);
        }

        if (steps.Count > options.MaxActions)
        {
            issues.Add(new PlanValidationIssue(
                AetherErrorCodes.PlanStepsTooMany,
                $"Plan contains {steps.Count} steps; the configured maximum is {options.MaxActions}.",
                null));
        }

        var launchedApps = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step is null)
            {
                issues.Add(new PlanValidationIssue(AetherErrorCodes.PlanActionUnknown, "Step is missing.", index));
                continue;
            }

            if (!ActionKinds.TryParse(step.Action, out var kind))
            {
                issues.Add(new PlanValidationIssue(
                    AetherErrorCodes.PlanActionUnknown,
                    $"Step {index + 1} declares an unsupported action.",
                    index));
                continue;
            }

            ValidateFields(kind, step, index, issues);

            foreach (var content in EnumerateFreeText(step))
            {
                if (ContainsUnsafeContent(content))
                {
                    issues.Add(new PlanValidationIssue(
                        AetherErrorCodes.PlanUnsafeContent,
                        $"Step {index + 1} contains content that looks like a path, URL or shell fragment, which plans may not carry.",
                        index));
                }
            }

            ValidateReferences(kind, step, index, catalog, launchedApps, issues);
        }

        return new ActionPlanValidationResult(issues.Count == 0, issues);
    }

    private void ValidateFields(ActionKind kind, ActionPlanStep step, int index, List<PlanValidationIssue> issues)
    {
        switch (kind)
        {
            case ActionKind.LaunchApp:
                RequirePresent(step.AppId, AetherErrorCodes.PlanActionFieldsInvalid, "LaunchApp requires appId.", index, issues);
                Forbid(step.ContextRef, "LaunchApp does not accept contextRef.", index, issues);
                Forbid(step.Layout, "LaunchApp does not accept layout.", index, issues);
                Forbid(step.Mode, "LaunchApp does not accept mode.", index, issues);
                Forbid(step.StatusText, "LaunchApp does not accept statusText.", index, issues);
                break;
            case ActionKind.PublishContext:
                Forbid(step.AppId, "PublishContext does not accept appId.", index, issues);
                RequirePresent(step.ContextRef, AetherErrorCodes.PlanActionFieldsInvalid, "PublishContext requires contextRef.", index, issues);
                Forbid(step.Layout, "PublishContext does not accept layout.", index, issues);
                Forbid(step.Mode, "PublishContext does not accept mode.", index, issues);
                Forbid(step.StatusText, "PublishContext does not accept statusText.", index, issues);
                break;
            case ActionKind.ArrangeWindows:
                Forbid(step.AppId, "ArrangeWindows does not accept appId.", index, issues);
                Forbid(step.ContextRef, "ArrangeWindows does not accept contextRef.", index, issues);
                RequirePresent(step.Layout, AetherErrorCodes.PlanActionFieldsInvalid, "ArrangeWindows requires layout.", index, issues);
                Forbid(step.Mode, "ArrangeWindows does not accept mode.", index, issues);
                Forbid(step.StatusText, "ArrangeWindows does not accept statusText.", index, issues);
                break;
            case ActionKind.SummarizeContext:
                Forbid(step.AppId, "SummarizeContext does not accept appId.", index, issues);
                RequirePresent(step.ContextRef, AetherErrorCodes.PlanActionFieldsInvalid, "SummarizeContext requires contextRef.", index, issues);
                Forbid(step.Layout, "SummarizeContext does not accept layout.", index, issues);
                Forbid(step.StatusText, "SummarizeContext does not accept statusText.", index, issues);
                if (step.Mode is not null && !SummarizeModes.Supported.Contains(step.Mode))
                {
                    issues.Add(new PlanValidationIssue(
                        AetherErrorCodes.PlanModeUnsupported,
                        "SummarizeContext mode must be 'private' in this build.",
                        index));
                }

                break;
            case ActionKind.ShowStatus:
                Forbid(step.AppId, "ShowStatus does not accept appId.", index, issues);
                Forbid(step.ContextRef, "ShowStatus does not accept contextRef.", index, issues);
                Forbid(step.Layout, "ShowStatus does not accept layout.", index, issues);
                Forbid(step.Mode, "ShowStatus does not accept mode.", index, issues);
                if (string.IsNullOrWhiteSpace(step.StatusText) || step.StatusText.Length > PlanLimits.MaxStatusTextLength || step.StatusText.Any(char.IsControl))
                {
                    issues.Add(new PlanValidationIssue(
                        AetherErrorCodes.PlanStatusInvalid,
                        $"ShowStatus requires 1-{PlanLimits.MaxStatusTextLength} printable characters of status text.",
                        index));
                }

                break;
        }
    }

    private static void ValidateReferences(
        ActionKind kind,
        ActionPlanStep step,
        int index,
        PlanCatalog catalog,
        HashSet<string> launchedApps,
        List<PlanValidationIssue> issues)
    {
        switch (kind)
        {
            case ActionKind.LaunchApp:
                if (step.AppId is not null)
                {
                    if (!IsValidIdentifier(step.AppId, PlanLimits.MaxAppIdLength))
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanAppUnknown,
                            $"Step {index + 1} references an invalid app id.",
                            index));
                        break;
                    }

                    if (!catalog.Apps.TryGetValue(step.AppId, out var app))
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanAppUnknown,
                            $"Step {index + 1} references an app that is not registered in the allowlist.",
                            index));
                        break;
                    }

                    if (!app.Launchable)
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanAppNotLaunchable,
                            $"Step {index + 1} references an app that cannot be launched.",
                            index));
                        break;
                    }

                    if (!launchedApps.Add(step.AppId))
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanAppDuplicateLaunch,
                            $"Step {index + 1} launches an app that is already launched by this plan.",
                            index));
                    }
                }

                break;
            case ActionKind.PublishContext:
            case ActionKind.SummarizeContext:
                if (step.ContextRef is not null)
                {
                    if (!IsValidContextRef(step.ContextRef))
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanUnsafeContent,
                            $"Step {index + 1} contains an unsafe context reference.",
                            index));
                        break;
                    }

                    if (!catalog.Contexts.ContainsKey(step.ContextRef))
                    {
                        issues.Add(new PlanValidationIssue(
                            AetherErrorCodes.PlanContextUnknown,
                            $"Step {index + 1} references a context that is not present in the trusted catalog.",
                            index));
                    }
                }

                break;
            case ActionKind.ArrangeWindows:
                if (step.Layout is not null && !Layouts.Supported.Contains(step.Layout))
                {
                    issues.Add(new PlanValidationIssue(
                        AetherErrorCodes.PlanLayoutUnsupported,
                        $"Step {index + 1} requests an unsupported window layout.",
                        index));
                }

                break;
        }
    }

    private static IEnumerable<string?> EnumerateFreeText(ActionPlanStep step)
    {
        yield return step.AppId;
        yield return step.ContextRef;
        yield return step.Layout;
        yield return step.Mode;
        yield return step.StatusText;
    }

    private static bool ContainsUnsafeContent(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.Contains("://", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("::", StringComparison.Ordinal);
    }

    private static void RequirePresent(string? value, string code, string message, int index, List<PlanValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new PlanValidationIssue(code, message, index));
        }
    }

    private static void Forbid(string? value, string message, int index, List<PlanValidationIssue> issues)
    {
        if (value is not null)
        {
            issues.Add(new PlanValidationIssue(AetherErrorCodes.PlanActionFieldsInvalid, message, index));
        }
    }

    private static bool IsValidIdentifier(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= maxLength
        && IdentifierPattern().IsMatch(value);

    private static bool IsValidContextRef(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= PlanLimits.MaxContextRefLength
        && ContextRefPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextRefPattern();
}
