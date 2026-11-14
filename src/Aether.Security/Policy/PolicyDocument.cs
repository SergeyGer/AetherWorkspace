using Aether.Contracts;
using Aether.Contracts.Errors;

namespace Aether.Security.Policy;

/// <summary>Trusted, versioned policy document. Cloud is denied by default.</summary>
public sealed class PolicyDocument
{
    public string PolicyVersion { get; init; } = AetherVersions.PolicyVersion;

    public CloudPolicy Cloud { get; init; } = new();

    /// <summary>Rules keyed by classification wire name (PUBLIC/INTERNAL/RESTRICTED/UNKNOWN).</summary>
    public Dictionary<string, DataClassRule> DataClasses { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string FailureMode { get; init; } = PolicyWireNames.FailClosed;

    public AuditPolicyDocument Audit { get; init; } = new();

    public PatternPolicyDocument Patterns { get; init; } = new();

    public static PolicyDocument CreateDefault() => new()
    {
        DataClasses = new Dictionary<string, DataClassRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["PUBLIC"] = new() { Cloud = PolicyWireNames.OptIn },
            ["INTERNAL"] = new() { Cloud = PolicyWireNames.DenyByDefault },
            ["RESTRICTED"] = new() { Cloud = PolicyWireNames.Deny },
            ["UNKNOWN"] = new() { Cloud = PolicyWireNames.Deny },
        },
    };

    /// <summary>Validates the invariants required for fail-closed operation.</summary>
    public bool TryValidate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(PolicyVersion))
        {
            error = "policyVersion must be set.";
            return false;
        }

        if (!string.Equals(FailureMode, PolicyWireNames.FailClosed, StringComparison.OrdinalIgnoreCase))
        {
            error = "failureMode must be FAIL_CLOSED.";
            return false;
        }

        if (Cloud is null)
        {
            error = "cloud section is required.";
            return false;
        }

        if (DataClasses is null)
        {
            error = "dataClasses section is required.";
            return false;
        }

        var required = new[] { "PUBLIC", "INTERNAL", "RESTRICTED", "UNKNOWN" };
        foreach (var name in required)
        {
            if (!DataClasses.TryGetValue(name, out var rule) || rule is null)
            {
                error = $"dataClasses.{name} is required.";
                return false;
            }

            if (rule.Cloud is not (PolicyWireNames.OptIn or PolicyWireNames.DenyByDefault or PolicyWireNames.Deny))
            {
                error = $"dataClasses.{name}.cloud must be OPT_IN, DENY_BY_DEFAULT or DENY.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public PolicyDocument EnsureValid()
    {
        if (!TryValidate(out var error))
        {
            throw new AetherContractException(AetherErrorCodes.PolicyInvalidDocument, error!);
        }

        return this;
    }

    /// <summary>Cloud-related switches.</summary>
    public sealed class CloudPolicy
    {
        public bool EnabledByDefault { get; init; }

        public bool RequireExplicitConsent { get; init; } = true;

        /// <summary>
        /// Explicit review flag required to permit sanitized INTERNAL cloud calls.
        /// Disabled by default because sanitization is never a blanket guarantee.
        /// </summary>
        public bool AllowSanitizedInternal { get; init; }

        public int RequestTimeoutSeconds { get; init; } = 30;
    }

    public sealed class DataClassRule
    {
        public string Cloud { get; init; } = PolicyWireNames.Deny;
    }

    public sealed class AuditPolicyDocument
    {
        public bool RawPayloads { get; init; }

        public bool LocalOnly { get; init; } = true;

        public string? AuditFile { get; init; }

        public int RetentionDays { get; init; } = 7;
    }

    public sealed class PatternPolicyDocument
    {
        public string AccountId { get; init; } = @"ACC-[0-9]{8,12}";
    }
}
