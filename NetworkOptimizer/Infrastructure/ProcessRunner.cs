using System.Diagnostics;
using System.Text;

namespace NetworkOptimizer.Infrastructure;

/// <summary>
/// Ejecución controlada y asíncrona de herramientas del sistema sin bloquear la UI ni comandos arbitrarios.
/// </summary>
public static class ProcessRunner
{
    public static async Task<(bool Success, int ExitCode, string Output, string Error)> RunAsync(
        string executable,
        string arguments,
        int timeoutMs = 10000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, process.ExitCode, output.Trim(), error.Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, -1, string.Empty, "La operación excedió el tiempo límite permitido.");
        }
        catch (Exception ex)
        {
            return (false, -1, string.Empty, ex.Message);
        }
    }
}
