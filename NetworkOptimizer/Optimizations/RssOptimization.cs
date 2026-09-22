using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics;
using NetworkOptimizer.Infrastructure;

namespace NetworkOptimizer.Optimizations;

public class RssOptimization : IOptimization
{
    private readonly NetworkInfoService _netInfoService = new();

    public string Id => "adapter_rss";
    public string Name => "Receive-Side Scaling (RSS)";
    public string Description => "Distribuye el procesamiento de red entre los múltiples núcleos de la CPU para evitar cuellos de botella en el Core 0.";
    public string TechnicalExplanation =>
        "Receive-Side Scaling (RSS) permite que la tarjeta de red genere interrupciones y asigne colas de procesamiento a diferentes núcleos de CPU. " +
        "En sistemas modernos de gaming, tener RSS activo evita caídas de FPS e hipos de latencia DPC provocados por sobrecarga en un único hilo. " +
        "Esta optimización solo actúa si el adaptador de red físico y su controlador soportan RSS.";

    public OptimizationCategory Category => OptimizationCategory.NetworkAdapter;
    public bool RequiresAdmin => true;
    public bool RequiresReboot => false;

    public bool CanRollback => !string.IsNullOrEmpty(OriginalState);
    public string? OriginalState => BackupStore.GetOriginalState(Id);
    public string? CurrentState { get; private set; }
    public bool IsSupported { get; private set; } = true;
    public string? TargetAdapterName { get; private set; }

    public async Task<string> RefreshCurrentStateAsync()
    {
        try
        {
            var primaryAdapter = _netInfoService.GetPrimaryActiveAdapter();
            if (primaryAdapter == null)
            {
                CurrentState = "Sin adaptador activo";
                IsSupported = false;
                return CurrentState;
            }

            TargetAdapterName = primaryAdapter.Name;

            var (success, _, output, _) = await ProcessRunner.RunAsync(
                "powershell.exe",
                $"-NoProfile -Command \"try {{ (Get-NetAdapterRss -Name '{primaryAdapter.Name}' -ErrorAction Stop).Enabled }} catch {{ 'NotSupported' }}\"",
                6000);

            if (success)
            {
                var clean = output.Trim();
                if (clean.Equals("True", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentState = "Habilitado (Activo)";
                    IsSupported = true;
                }
                else if (clean.Equals("False", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentState = "Deshabilitado";
                    IsSupported = true;
                }
                else
                {
                    CurrentState = "No soportado por el hardware/driver";
                    IsSupported = false;
                }
                return CurrentState;
            }

            CurrentState = "Desconocido";
            return CurrentState;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RssOptimization", $"Error al consultar RSS: {ex.Message}");
            CurrentState = "Error al consultar";
            return CurrentState;
        }
    }

    public async Task<OperationResult> ApplyAsync()
    {
        await RefreshCurrentStateAsync();

        if (!IsSupported)
        {
            return OperationResult.Fail(Name, "El adaptador activo no soporta Receive-Side Scaling (RSS). No se aplicaron cambios.");
        }

        if (CurrentState == "Habilitado (Activo)")
        {
            return OperationResult.Ok(Name, "RSS ya se encuentra habilitado en el adaptador activo.", CurrentState, CurrentState);
        }

        if (string.IsNullOrEmpty(TargetAdapterName))
        {
            return OperationResult.Fail(Name, "No se identificó el adaptador activo.");
        }

        // Guardar estado original
        BackupStore.SaveOriginalState(Id, CurrentState ?? "Deshabilitado");
        string previousState = CurrentState ?? "Desconocido";

        var psCommand = $"Enable-NetAdapterRss -Name '{TargetAdapterName}' -Confirm:$false";

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, _, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"");

            if (elevatedOk)
            {
                await RefreshCurrentStateAsync();
                var res = OperationResult.Ok(Name, $"RSS habilitado con éxito en '{TargetAdapterName}' mediante elevación UAC.", previousState, CurrentState);
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail(Name, "No se pudo habilitar RSS en el adaptador.", err, previousState);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, _, errorText) = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -Command \"{psCommand}\"",
            6000);

        if (ok && exitCode == 0)
        {
            await RefreshCurrentStateAsync();
            var res = OperationResult.Ok(Name, $"RSS habilitado con éxito en '{TargetAdapterName}'.", previousState, CurrentState);
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail(Name, "Fallo al habilitar RSS.", errorText, previousState);
            AppLogger.LogResult(res);
            return res;
        }
    }

    public async Task<OperationResult> RollbackAsync()
    {
        var targetOriginal = OriginalState;
        if (string.IsNullOrEmpty(targetOriginal))
        {
            return OperationResult.Fail(Name, "No existe un estado original respaldado para revertir.");
        }

        await RefreshCurrentStateAsync();
        string previousState = CurrentState ?? "Habilitado (Activo)";

        if (string.IsNullOrEmpty(TargetAdapterName))
        {
            return OperationResult.Fail(Name, "No se identificó el adaptador activo.");
        }

        bool shouldEnable = targetOriginal.Contains("Habilitado", StringComparison.OrdinalIgnoreCase) || targetOriginal.Equals("True", StringComparison.OrdinalIgnoreCase);
        var psCommand = shouldEnable
            ? $"Enable-NetAdapterRss -Name '{TargetAdapterName}' -Confirm:$false"
            : $"Disable-NetAdapterRss -Name '{TargetAdapterName}' -Confirm:$false";

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, _, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"");

            if (elevatedOk)
            {
                BackupStore.ClearOriginalState(Id);
                await RefreshCurrentStateAsync();
                var res = OperationResult.Ok(Name, $"RSS revertido exitosamente al estado original: {targetOriginal}", previousState, CurrentState);
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail(Name, "Fallo al revertir RSS.", err, previousState);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, _, errorText) = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -Command \"{psCommand}\"",
            6000);

        if (ok && exitCode == 0)
        {
            BackupStore.ClearOriginalState(Id);
            await RefreshCurrentStateAsync();
            var res = OperationResult.Ok(Name, $"RSS revertido exitosamente al estado original: {targetOriginal}", previousState, CurrentState);
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail(Name, "Fallo al revertir RSS.", errorText, previousState);
            AppLogger.LogResult(res);
            return res;
        }
    }
}
