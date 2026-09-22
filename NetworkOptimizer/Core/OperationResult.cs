namespace NetworkOptimizer.Core;

/// <summary>
/// Representa el resultado de cualquier operación de diagnóstico, optimización o mantenimiento.
/// </summary>
public record OperationResult
{
    public bool Success { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PreviousState { get; init; }
    public string? NewState { get; init; }
    public string? Error { get; init; }

    public static OperationResult Ok(string operation, string message, string? previousState = null, string? newState = null) =>
        new()
        {
            Success = true,
            Operation = operation,
            Message = message,
            PreviousState = previousState,
            NewState = newState
        };

    public static OperationResult Fail(string operation, string message, string? error = null, string? previousState = null) =>
        new()
        {
            Success = false,
            Operation = operation,
            Message = message,
            Error = error,
            PreviousState = previousState
        };
}
