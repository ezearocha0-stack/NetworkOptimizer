using NetworkOptimizer.Core;
using NetworkOptimizer.Infrastructure;

namespace NetworkOptimizer.Optimizations;

public class TcpAutoTuningOptimization : IOptimization
{
    public string Id => "tcp_autotuning";
    public string Name => "TCP Window Auto-Tuning";
    public string Description => "Comprueba y restaura el nivel estándar óptimo (Normal) de la ventana de recepción TCP.";
    public string TechnicalExplanation =>
        "El Auto-Tuning (RFC 1323) calcula dinámicamente el tamaño óptimo de la ventana TCP para aprovechar el ancho de banda contratado. " +
        "Si alguna herramienta o guía de internet lo configuró en 'Disabled', la velocidad de descarga se limita drásticamente a 64 KB por conexión. " +
        "Esta optimización no aplica tweaks agresivos: verifica el estado y asegura el estándar 'Normal'.";

    public OptimizationCategory Category => OptimizationCategory.TcpIp;
    public bool RequiresAdmin => true;
    public bool RequiresReboot => false;

    public bool CanRollback => !string.IsNullOrEmpty(OriginalState);
    public string? OriginalState => BackupStore.GetOriginalState(Id);
    public string? CurrentState { get; private set; }

    public async Task<string> RefreshCurrentStateAsync()
    {
        try
        {
            var (success, _, output, _) = await ProcessRunner.RunAsync(
                "powershell.exe",
                "-NoProfile -Command \"(Get-NetTCPSetting -SettingName InternetCustom, Datacenter, Internet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty AutoTuningLevelLocal -First 1)\"",
                6000);

            if (success && !string.IsNullOrWhiteSpace(output))
            {
                CurrentState = output.Trim();
                return CurrentState;
            }

            // Fallback netsh
            var (netshOk, _, netshOut, _) = await ProcessRunner.RunAsync("netsh.exe", "int tcp show global", 5000);
            if (netshOk)
            {
                if (netshOut.Contains("normal", StringComparison.OrdinalIgnoreCase)) CurrentState = "Normal";
                else if (netshOut.Contains("disabled", StringComparison.OrdinalIgnoreCase) || netshOut.Contains("desactivado", StringComparison.OrdinalIgnoreCase)) CurrentState = "Disabled";
                else if (netshOut.Contains("restricted", StringComparison.OrdinalIgnoreCase)) CurrentState = "Restricted";
                else CurrentState = "Detectado (netsh)";
                return CurrentState;
            }

            CurrentState = "Desconocido";
            return CurrentState;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TcpAutoTuning", $"Error al consultar estado: {ex.Message}");
            CurrentState = "Error al consultar";
            return CurrentState;
        }
    }

    public async Task<OperationResult> ApplyAsync()
    {
        await RefreshCurrentStateAsync();

        if (string.Equals(CurrentState, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Ok(Name, "El TCP Auto-Tuning ya se encuentra en el estado óptimo (Normal). No requiere cambios.", CurrentState, CurrentState);
        }

        // Guardar estado original antes de modificar
        if (!string.IsNullOrEmpty(CurrentState) && CurrentState != "Desconocido" && CurrentState != "Error al consultar")
        {
            BackupStore.SaveOriginalState(Id, CurrentState);
        }

        string previousState = CurrentState ?? "Desconocido";

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, code, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "netsh.exe",
                "int tcp set global autotuninglevel=normal");

            if (elevatedOk)
            {
                await RefreshCurrentStateAsync();
                var res = OperationResult.Ok(Name, "TCP Auto-Tuning restaurado a 'Normal' mediante elevación UAC.", previousState, CurrentState);
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail(Name, "No se pudo modificar TCP Auto-Tuning.", err, previousState);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, outText, errorText) = await ProcessRunner.RunAsync(
            "netsh.exe",
            "int tcp set global autotuninglevel=normal",
            5000);

        if (ok && exitCode == 0)
        {
            await RefreshCurrentStateAsync();
            var res = OperationResult.Ok(Name, "TCP Auto-Tuning restaurado correctamente a 'Normal'.", previousState, CurrentState);
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail(Name, "Fallo al aplicar TCP Auto-Tuning a 'Normal'.", errorText, previousState);
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
        string previousState = CurrentState ?? "Normal";
        string levelParam = targetOriginal.ToLowerInvariant();

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, _, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "netsh.exe",
                $"int tcp set global autotuninglevel={levelParam}");

            if (elevatedOk)
            {
                BackupStore.ClearOriginalState(Id);
                await RefreshCurrentStateAsync();
                var res = OperationResult.Ok(Name, $"TCP Auto-Tuning revertido exitosamente al estado original: {targetOriginal}", previousState, CurrentState);
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail(Name, "Fallo al revertir TCP Auto-Tuning.", err, previousState);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, _, errorText) = await ProcessRunner.RunAsync(
            "netsh.exe",
            $"int tcp set global autotuninglevel={levelParam}",
            5000);

        if (ok && exitCode == 0)
        {
            BackupStore.ClearOriginalState(Id);
            await RefreshCurrentStateAsync();
            var res = OperationResult.Ok(Name, $"TCP Auto-Tuning revertido exitosamente al estado original: {targetOriginal}", previousState, CurrentState);
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail(Name, "Fallo al revertir TCP Auto-Tuning.", errorText, previousState);
            AppLogger.LogResult(res);
            return res;
        }
    }
}
