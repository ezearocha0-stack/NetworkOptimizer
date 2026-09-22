using System.Diagnostics;
using System.Security.Principal;

namespace NetworkOptimizer.Infrastructure;

/// <summary>
/// Helper para detección de privilegios de administrador y elevación granular vía UAC.
/// </summary>
public static class AdminElevationHelper
{
    private static bool? _isElevatedCached;

    /// <summary>
    /// Comprueba si el proceso actual tiene permisos de Administrador.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            if (_isElevatedCached.HasValue) return _isElevatedCached.Value;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                _isElevatedCached = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                _isElevatedCached = false;
            }

            return _isElevatedCached.Value;
        }
    }

    /// <summary>
    /// Ejecuta un comando elevado únicamente cuando se requiere UAC sin forzar toda la app.
    /// </summary>
    public static async Task<(bool Success, int ExitCode, string Output, string Error)> RunElevatedAsync(
        string executable,
        string arguments,
        int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = true, // Requerido para Verb = "runas"
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, -1, string.Empty, "No se pudo iniciar el proceso elevado o el usuario canceló el UAC.");
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode == 0, process.ExitCode, $"Proceso completado con código {process.ExitCode}", string.Empty);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // El usuario canceló la elevación UAC (ERROR_CANCELLED)
            return (false, 1223, string.Empty, "Operación cancelada por el usuario en la solicitud de control de cuentas (UAC).");
        }
        catch (Exception ex)
        {
            return (false, -1, string.Empty, $"Error al ejecutar comando elevado: {ex.Message}");
        }
    }

    /// <summary>
    /// Reinicia la aplicación solicitando elevación de administrador completa.
    /// </summary>
    public static bool RestartElevated()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
