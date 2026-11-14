using Aether.Contracts.Models;

namespace Aether.Execution.Executor;

/// <summary>Bounded execution limits. Values can be tightened per deployment.</summary>
public sealed record ExecutionOptions
{
    public int MaxActionsPerPlan { get; init; } = PlanLimits.DefaultMaxActions;

    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan PlanTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan CompensationTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public bool ExecuteCompensationsOnFailure { get; init; } = true;
}
