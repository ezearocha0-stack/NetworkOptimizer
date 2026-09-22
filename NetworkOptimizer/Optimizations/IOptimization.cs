using NetworkOptimizer.Core;

namespace NetworkOptimizer.Optimizations;

public enum OptimizationCategory
{
    TcpIp,
    NetworkAdapter,
    Dns,
    System
}

public interface IOptimization
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string TechnicalExplanation { get; }
    OptimizationCategory Category { get; }
    bool RequiresAdmin { get; }
    bool RequiresReboot { get; }

    bool CanRollback { get; }
    string? OriginalState { get; }
    string? CurrentState { get; }

    Task<string> RefreshCurrentStateAsync();
    Task<OperationResult> ApplyAsync();
    Task<OperationResult> RollbackAsync();
}
