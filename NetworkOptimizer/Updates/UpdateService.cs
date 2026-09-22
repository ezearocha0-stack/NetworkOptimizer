using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NetworkOptimizer.Config;
using NetworkOptimizer.Core;
using NetworkOptimizer.Updates.Models;

namespace NetworkOptimizer.Updates;

public record UpdateCheckResult(
    bool IsAvailable,
    UpdateManifest? Manifest,
    string? NewVersion,
    string Message);

public record UpdateDownloadResult(
    bool Success,
    string? DownloadedFilePath,
    string? ErrorMessage);

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, disposeClient: true)
    {
    }

    public UpdateService(HttpClient httpClient, bool disposeClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeClient = disposeClient;
    }

    /// <summary>
    /// Comprueba en segundo plano si existe una versión más reciente publicada en el manifest.
    /// Nunca bloquea el inicio de la aplicación y maneja silenciosamente caídas de red u offline.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        string? manifestUrl = null,
        string? currentVersion = null,
        CancellationToken ct = default)
    {
        var url = manifestUrl ?? AppConfig.UpdateManifestUrl;
        var current = currentVersion ?? AppConfig.AppVersion;

        try
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                AppLogger.Info("Actualizador", $"URL de manifiesto no configurada o inválida: {url}");
                return new UpdateCheckResult(false, null, null, "URL de manifiesto inválida.");
            }

            AppLogger.Info("Actualizador", $"Consultando manifiesto de actualización en {url}...");

            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Info("Actualizador", $"El servidor respondió con código HTTP {(int)response.StatusCode}.");
                return new UpdateCheckResult(false, null, null, $"No se pudo consultar el servidor de actualizaciones (HTTP {(int)response.StatusCode}).");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var manifest = ParseManifest(json);
            if (manifest == null)
            {
                AppLogger.Warn("Actualizador", "Manifiesto de actualización con estructura inválida o campos faltantes.");
                return new UpdateCheckResult(false, null, null, "Manifiesto remoto no válido.");
            }

            // Comparar versiones numéricamente
            if (UpdateVersionComparator.IsNewerVersion(current, manifest.Version))
            {
                AppLogger.Success("Actualizador", $"Nueva versión detectada: v{manifest.Version} (Actual: v{current})");
                return new UpdateCheckResult(true, manifest, manifest.Version, $"Nueva versión disponible: v{manifest.Version}");
            }

            AppLogger.Info("Actualizador", $"La versión actual (v{current}) está al día.");
            return new UpdateCheckResult(false, manifest, manifest.Version, "La aplicación está actualizada.");
        }
        catch (Exception ex)
        {
            AppLogger.Info("Actualizador", $"Comprobación de actualización omitida o sin conexión: {ex.Message}");
            return new UpdateCheckResult(false, null, null, "No se pudo comprobar actualizaciones (sin conexión).");
        }
    }

    /// <summary>
    /// Parsea y valida el contenido JSON del manifiesto de actualización.
    /// </summary>
    public static UpdateManifest? ParseManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, options);

            if (manifest == null) return null;

            if (string.IsNullOrWhiteSpace(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.DownloadUrl) ||
                string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                return null;
            }

            // Validar formato de versión
            if (!UpdateVersionComparator.TryParseNormalized(manifest.Version, out _))
            {
                return null;
            }

            // Validar que la URL de descarga sea HTTPS seguro
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            // Validar que el SHA-256 tenga 64 caracteres hexadecimales
            var cleanHash = manifest.Sha256.Trim().Replace("-", string.Empty);
            if (cleanHash.Length != 64)
            {
                return null;
            }

            foreach (var c in cleanHash)
            {
                if (!char.IsAsciiHexDigit(c))
                {
                    return null;
                }
            }

            return manifest with { Sha256 = cleanHash.ToLowerInvariant() };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Descarga el paquete a un archivo temporal y valida su integridad SHA-256.
    /// Si el hash no coincide, el archivo temporal es eliminado inmediatamente.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadAndVerifyUpdateAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UpdateDownloadResult(false, null, "La URL de descarga no es HTTPS válida.");
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"NetworkOptimizer_v{manifest.Version}_{Guid.NewGuid():N}.tmp");

        try
        {
            AppLogger.Info("Actualizador", $"Iniciando descarga segura de actualización v{manifest.Version}...");

            using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        var pct = (double)totalRead / totalBytes.Value * 100.0;
                        progress?.Report(Math.Clamp(pct, 0.0, 100.0));
                    }
                }
            }

            AppLogger.Info("Actualizador", "Descarga completada. Verificando integridad criptográfica SHA-256...");

            // Verificación criptográfica SHA-256 estricta
            if (!UpdateHashVerifier.VerifySha256(tempFilePath, manifest.Sha256))
            {
                AppLogger.Warn("Actualizador", "¡ALERTA DE SEGURIDAD! El hash SHA-256 del archivo descargado no coincide con el manifiesto. Eliminando paquete.");
                TryDeleteFile(tempFilePath);
                return new UpdateDownloadResult(false, null, "El hash de seguridad SHA-256 no coincide con el manifiesto oficial.");
            }

            AppLogger.Success("Actualizador", "Verificación SHA-256 exitosa. Paquete íntegro y listo para reemplazo.");
            return new UpdateDownloadResult(true, tempFilePath, null);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Actualizador", "Error durante la descarga de la actualización", ex.Message);
            TryDeleteFile(tempFilePath);
            return new UpdateDownloadResult(false, null, $"Error de descarga: {ex.Message}");
        }
    }

    /// <summary>
    /// Ejecuta NetworkOptimizer.Updater.exe con los argumentos correspondientes y finaliza NetworkOptimizer.
    /// </summary>
    public bool LaunchUpdaterAndExit(string downloadedPackagePath, bool restart = true)
    {
        try
        {
            if (!File.Exists(downloadedPackagePath))
            {
                AppLogger.Error("Actualizador", "El archivo temporal de actualización no existe.", downloadedPackagePath);
                return false;
            }

            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
            {
                AppLogger.Error("Actualizador", "No se pudo determinar la ruta del ejecutable actual.", string.Empty);
                return false;
            }

            var baseDir = AppContext.BaseDirectory;
            var updaterPath = Path.Combine(baseDir, AppConfig.UpdaterExecutableName);

            // Si no está en el directorio base actual, buscar en subdirectorios o en la carpeta del ejecutable
            if (!File.Exists(updaterPath))
            {
                var exeDir = Path.GetDirectoryName(currentExePath) ?? baseDir;
                updaterPath = Path.Combine(exeDir, AppConfig.UpdaterExecutableName);
            }

            if (!File.Exists(updaterPath))
            {
                AppLogger.Error("Actualizador", $"No se encontró el ejecutable del actualizador: {updaterPath}", string.Empty);
                return false;
            }

            var currentPid = Environment.ProcessId;
            var restartFlag = restart ? "--restart" : string.Empty;
            var arguments = $"--target \"{currentExePath}\" --package \"{downloadedPackagePath}\" --pid {currentPid} {restartFlag}";

            AppLogger.Info("Actualizador", $"Iniciando {AppConfig.UpdaterExecutableName} para aplicar actualización...");

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(startInfo);

            // Salir ordenadamente de la aplicación actual
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            else
            {
                Environment.Exit(0);
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Actualizador", "Fallo al iniciar el proceso de actualización", ex.Message);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignorar errores al limpiar archivos temporales
        }
    }
}
