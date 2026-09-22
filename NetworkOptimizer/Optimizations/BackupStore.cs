using System.IO;
using System.Text.Json;
using NetworkOptimizer.Core;

namespace NetworkOptimizer.Optimizations;

public static class BackupStore
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetworkOptimizer");

    private static readonly string BackupFilePath = Path.Combine(StorageDirectory, "state_backups.json");
    private static readonly object FileLock = new();

    public static void SaveOriginalState(string optimizationId, string originalState)
    {
        lock (FileLock)
        {
            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }

                var backups = LoadAllBackups();
                // Guardar solo la primera vez para mantener el valor verdaderamente original
                if (!backups.ContainsKey(optimizationId))
                {
                    backups[optimizationId] = originalState;
                    var json = JsonSerializer.Serialize(backups, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(BackupFilePath, json);
                    AppLogger.Info("BackupStore", $"Punto de restauración guardado para [{optimizationId}]: {originalState}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("BackupStore", $"No se pudo persistir el punto de restauración: {ex.Message}");
            }
        }
    }

    public static string? GetOriginalState(string optimizationId)
    {
        lock (FileLock)
        {
            try
            {
                var backups = LoadAllBackups();
                return backups.TryGetValue(optimizationId, out var state) ? state : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public static void ClearOriginalState(string optimizationId)
    {
        lock (FileLock)
        {
            try
            {
                var backups = LoadAllBackups();
                if (backups.Remove(optimizationId))
                {
                    var json = JsonSerializer.Serialize(backups, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(BackupFilePath, json);
                }
            }
            catch { }
        }
    }

    private static Dictionary<string, string> LoadAllBackups()
    {
        if (!File.Exists(BackupFilePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(BackupFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
