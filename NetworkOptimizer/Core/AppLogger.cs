using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

namespace NetworkOptimizer.Core;

public record LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Level { get; init; } = "INFO"; // INFO, SUCCESS, WARN, ERROR
    public string Operation { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PreviousState { get; init; }
    public string? NewState { get; init; }
    public string? Error { get; init; }

    public string DisplayText
    {
        get
        {
            var text = $"[{Timestamp:HH:mm:ss}] [{Level}] {Operation}: {Message}";
            if (!string.IsNullOrEmpty(PreviousState) || !string.IsNullOrEmpty(NewState))
            {
                text += $" (Anterior: {PreviousState ?? "N/A"} -> Nuevo: {NewState ?? "N/A"})";
            }
            if (!string.IsNullOrEmpty(Error))
            {
                text += $" | Error: {Error}";
            }
            return text;
        }
    }
}

/// <summary>
/// Gestor de logs thread-safe y sanitizado de NetworkOptimizer.
/// </summary>
public static class AppLogger
{
    private static readonly object _lock = new();
    private static readonly ObservableCollection<LogEntry> _entries = new();
    private static readonly ReadOnlyObservableCollection<LogEntry> _readOnlyEntries = new(_entries);

    public static ReadOnlyObservableCollection<LogEntry> Entries => _readOnlyEntries;

    public static event Action<LogEntry>? OnLogAdded;

    public static void Info(string operation, string message) =>
        Log("INFO", operation, message);

    public static void Success(string operation, string message, string? previousState = null, string? newState = null) =>
        Log("SUCCESS", operation, message, previousState, newState);

    public static void Warn(string operation, string message) =>
        Log("WARN", operation, message);

    public static void Error(string operation, string message, string? error = null) =>
        Log("ERROR", operation, message, null, null, error);

    public static void LogResult(OperationResult result)
    {
        if (result.Success)
        {
            Success(result.Operation, result.Message, result.PreviousState, result.NewState);
        }
        else
        {
            Error(result.Operation, result.Message, result.Error);
        }
    }

    private static void Log(string level, string operation, string message, string? previousState = null, string? newState = null, string? error = null)
    {
        // Sanitizar para evitar registrar claves, credenciales o tokens
        var sanitizedMessage = Sanitize(message);
        var sanitizedError = Sanitize(error);

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Operation = operation,
            Message = sanitizedMessage,
            PreviousState = previousState,
            NewState = newState,
            Error = sanitizedError
        };

        lock (_lock)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => AddEntry(entry));
            }
            else
            {
                AddEntry(entry);
            }
        }
    }

    private static void AddEntry(LogEntry entry)
    {
        _entries.Add(entry);
        // Limitar a los últimos 500 logs en memoria
        if (_entries.Count > 500)
        {
            _entries.RemoveAt(0);
        }
        OnLogAdded?.Invoke(entry);
    }

    public static void Clear()
    {
        lock (_lock)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => _entries.Clear());
            }
            else
            {
                _entries.Clear();
            }
        }
    }

    /// <summary>
    /// Elimina patrones de claves de licencia (ej: XXXX-XXXX-XXXX-XXXX) o tokens de los logs.
    /// </summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Máscara para patrones de licencia tipo alfanumérico en grupos con guiones
        var pattern = @"\b([A-Z0-9]{4,5}-){2,}[A-Z0-9]{4,5}\b";
        return Regex.Replace(text, pattern, "[CLAVE_OCULTA]", RegexOptions.IgnoreCase);
    }
}
