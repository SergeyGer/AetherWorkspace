namespace Aether.Contracts.Errors;

/// <summary>
/// Structured error and denial codes shared across contracts, policy, execution,
/// broker and AI components. Codes are stable API surface for tests and UI.
/// </summary>
public static class AetherErrorCodes
{
    // Plan validation
    public const string PlanInvalidJson = "plan.invalid_json";
    public const string PlanSchemaVersionUnsupported = "plan.schema.version_unsupported";
    public const string PlanIdInvalid = "plan.plan_id.invalid";
    public const string PlanStepsEmpty = "plan.steps.empty";
    public const string PlanStepsTooMany = "plan.steps.too_many";
    public const string PlanActionUnknown = "plan.action.unknown";
    public const string PlanActionFieldsInvalid = "plan.action.fields_invalid";
    public const string PlanAppUnknown = "plan.app.unknown";
    public const string PlanAppNotLaunchable = "plan.app.not_launchable";
    public const string PlanAppDuplicateLaunch = "plan.app.duplicate_launch";
    public const string PlanContextUnknown = "plan.context.unknown";
    public const string PlanLayoutUnsupported = "plan.layout.unsupported";
    public const string PlanModeUnsupported = "plan.mode.unsupported";
    public const string PlanStatusInvalid = "plan.status.invalid";
    public const string PlanUnsafeContent = "plan.unsafe_content";

    // Authorization / sessions
    public const string AuthSessionInvalid = "auth.session.invalid";
    public const string AuthSessionExpired = "auth.session.expired";
    public const string AuthCapabilityMissing = "auth.capability.missing";
    public const string AuthChannelDenied = "auth.channel.denied";
    public const string AuthClassificationDenied = "auth.classification.denied";
    public const string AuthContextTypeDenied = "auth.context_type.denied";
    public const string AuthTokenInvalid = "auth.token.invalid";
    public const string AuthTokenReplayed = "auth.token.replayed";
    public const string AuthIdentityMismatch = "auth.identity.mismatch";
    public const string AuthOperatorTokenInvalid = "auth.operator_token.invalid";
    public const string AuthProtocolUnsupported = "auth.protocol.unsupported";

    // Policy / classification / egress
    public const string PolicyClassificationUnknown = "policy.classification.unknown";
    public const string PolicyCloudDisabled = "policy.cloud.disabled";
    public const string PolicyCloudConsentRequired = "policy.cloud.consent_required";
    public const string PolicyCloudDenied = "policy.cloud.denied";
    public const string PolicyCloudDeniedByDefault = "policy.cloud.denied_by_default";
    public const string PolicyInvalidDocument = "policy.invalid_document";
    public const string EgressDenied = "egress.denied";
    public const string EgressProviderError = "egress.provider.error";
    public const string EgressResponseInvalid = "egress.response.invalid";

    // Broker
    public const string BrokerRegistryInvalid = "broker.registry.invalid";
    public const string BrokerAppUnknown = "broker.app.unknown";
    public const string BrokerAppAlreadyRunning = "broker.app.already_running";
    public const string BrokerExecutableDenied = "broker.executable.denied";
    public const string BrokerLaunchFailed = "broker.launch.failed";
    public const string BrokerLaunchTimeout = "broker.launch.timeout";
    public const string BrokerPayloadTooLarge = "broker.payload.too_large";
    public const string BrokerContextNotFound = "broker.context.not_found";
    public const string BrokerDuplicateMessage = "broker.duplicate_message";
    public const string BrokerQueueOverflow = "broker.queue.overflow";
    public const string BrokerArrangeNoWindows = "broker.arrange.no_windows";
    public const string BrokerNotReady = "broker.not_ready";

    // AI providers
    public const string AiProviderUnavailable = "ai.provider.unavailable";
    public const string AiProviderMissing = "ai.provider.missing";
    public const string AiOutputInvalid = "ai.output.invalid";
    public const string AiCommandUnsupported = "ai.command.unsupported";
    public const string AiCloudNotConfigured = "ai.cloud.not_configured";

    // Execution
    public const string ExecutionDenied = "execution.denied";
    public const string ExecutionFailed = "execution.failed";
    public const string ExecutionCancelled = "execution.cancelled";
    public const string ExecutionDeadlineExceeded = "execution.deadline_exceeded";
    public const string ExecutionInternalError = "execution.internal_error";

    // Workflow
    public const string WorkflowDenied = "workflow.denied";
    public const string WorkflowFailed = "workflow.failed";
    public const string WorkflowCancelled = "workflow.cancelled";

    // Audit
    public const string AuditChainInvalid = "audit.chain.invalid";

    /// <summary>Maps a structured code to a stable user-facing denial category.</summary>
    public static DenialCategory GetDenialCategory(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return DenialCategory.Unknown;
        }

        var category = code.Split('.')[0];
        return category switch
        {
            "plan" => code switch
            {
                PlanActionUnknown => DenialCategory.UnsupportedAction,
                PlanAppUnknown or PlanAppNotLaunchable or PlanAppDuplicateLaunch => DenialCategory.UnauthorizedApp,
                PlanSchemaVersionUnsupported or PlanIdInvalid or PlanStepsEmpty or PlanStepsTooMany
                    or PlanActionFieldsInvalid or PlanContextUnknown or PlanLayoutUnsupported
                    or PlanModeUnsupported or PlanStatusInvalid or PlanInvalidJson => DenialCategory.InvalidPlan,
                PlanUnsafeContent => DenialCategory.InputRejected,
                _ => DenialCategory.InvalidPlan,
            },
            "auth" => code switch
            {
                AuthCapabilityMissing or AuthChannelDenied or AuthContextTypeDenied => DenialCategory.CapabilityMissing,
                AuthClassificationDenied => DenialCategory.UnsafeClassification,
                _ => DenialCategory.PolicyDenied,
            },
            "policy" or "egress" => code switch
            {
                PolicyClassificationUnknown => DenialCategory.UnsafeClassification,
                EgressProviderError or AiProviderUnavailable or AiProviderMissing or AiCloudNotConfigured => DenialCategory.ProviderUnavailable,
                _ => DenialCategory.PolicyDenied,
            },
            "broker" => DenialCategory.PolicyDenied,
            "ai" => DenialCategory.ProviderUnavailable,
            "execution" => DenialCategory.InternalError,
            "workflow" => DenialCategory.InternalError,
            _ => DenialCategory.Unknown,
        };
    }
}
