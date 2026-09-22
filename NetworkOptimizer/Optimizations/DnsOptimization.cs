using System.Text.Json;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics;
using NetworkOptimizer.Diagnostics.Models;
using NetworkOptimizer.Infrastructure;

namespace NetworkOptimizer.Optimizations;

public record DnsBackupState(bool WasDhcp, List<string> DnsServers);

public class DnsOptimization
{
    private readonly NetworkInfoService _netInfoService = new();

    public async Task<DnsBackupState?> GetCurrentDnsStateAsync(string adapterName)
    {
        try
        {
            var (success, _, output, _) = await ProcessRunner.RunAsync(
                "powershell.exe",
                $"-NoProfile -Command \"(Get-DnsClientServerAddress -InterfaceAlias '{adapterName}' -AddressFamily IPv4).ServerAddresses -join ','\"",
                5000);

            if (success)
            {
                var servers = output.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                // Si no tiene servidores estáticos asignados o está en DHCP
                var wasDhcp = servers.Count == 0;
                return new DnsBackupState(wasDhcp, servers);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<OperationResult> ApplyDnsPresetAsync(string adapterName, DnsServerPreset preset)
    {
        AppLogger.Info("DNS", $"Aplicando preset '{preset.ProviderName}' en el adaptador '{adapterName}'...");

        var backupKey = $"dns_backup_{adapterName}";

        // 1. Guardar estado original si no existe backup previo
        if (string.IsNullOrEmpty(BackupStore.GetOriginalState(backupKey)))
        {
            var currentState = await GetCurrentDnsStateAsync(adapterName);
            if (currentState != null)
            {
                var json = JsonSerializer.Serialize(currentState);
                BackupStore.SaveOriginalState(backupKey, json);
            }
        }

        string previousDisplay = BackupStore.GetOriginalState(backupKey) ?? "Desconocido";

        // Si el preset es restaurar DHCP
        if (preset.IsDhcp)
        {
            return await RestoreDhcpDnsAsync(adapterName);
        }

        // 2. Aplicar los servidores estáticos
        var dnsList = new List<string> { preset.PrimaryDns };
        if (!string.IsNullOrWhiteSpace(preset.SecondaryDns))
        {
            dnsList.Add(preset.SecondaryDns);
        }

        var addressesFormatted = string.Join(",", dnsList.Select(ip => $"'{ip}'"));
        var psCommand = $"Set-DnsClientServerAddress -InterfaceAlias '{adapterName}' -ServerAddresses @({addressesFormatted})";

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, _, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"");

            if (elevatedOk)
            {
                var res = OperationResult.Ok("DNS Preset", $"Servidores DNS de {preset.ProviderName} ({string.Join(", ", dnsList)}) aplicados correctamente mediante elevación UAC.", previousDisplay, string.Join(", ", dnsList));
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail("DNS Preset", $"No se pudo aplicar el DNS de {preset.ProviderName}.", err, previousDisplay);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, _, errorText) = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -Command \"{psCommand}\"",
            6000);

        if (ok && exitCode == 0)
        {
            var res = OperationResult.Ok("DNS Preset", $"Servidores DNS de {preset.ProviderName} ({string.Join(", ", dnsList)}) configurados exitosamente.", previousDisplay, string.Join(", ", dnsList));
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail("DNS Preset", $"Fallo al configurar servidores DNS.", errorText, previousDisplay);
            AppLogger.LogResult(res);
            return res;
        }
    }

    public async Task<OperationResult> RestoreDhcpDnsAsync(string adapterName)
    {
        AppLogger.Info("DNS", $"Restaurando DNS a modo automático (DHCP) en '{adapterName}'...");

        var psCommand = $"Set-DnsClientServerAddress -InterfaceAlias '{adapterName}' -ResetServerAddresses";

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedOk, _, _, err) = await AdminElevationHelper.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"");

            if (elevatedOk)
            {
                var res = OperationResult.Ok("DNS DHCP", $"DNS restaurado a automático (DHCP) en '{adapterName}' mediante elevación UAC.");
                AppLogger.LogResult(res);
                return res;
            }

            var failRes = OperationResult.Fail("DNS DHCP", "No se pudo restaurar DNS a DHCP.", err);
            AppLogger.LogResult(failRes);
            return failRes;
        }

        var (ok, exitCode, _, errorText) = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -Command \"{psCommand}\"",
            6000);

        if (ok && exitCode == 0)
        {
            var res = OperationResult.Ok("DNS DHCP", $"DNS restaurado a automático (DHCP) en '{adapterName}' exitosamente.");
            AppLogger.LogResult(res);
            return res;
        }
        else
        {
            var res = OperationResult.Fail("DNS DHCP", "Fallo al restaurar DNS a DHCP.", errorText);
            AppLogger.LogResult(res);
            return res;
        }
    }

    public async Task<OperationResult> RollbackDnsAsync(string adapterName)
    {
        var backupKey = $"dns_backup_{adapterName}";
        var backupJson = BackupStore.GetOriginalState(backupKey);

        if (string.IsNullOrEmpty(backupJson))
        {
            return OperationResult.Fail("Rollback DNS", "No existe un estado DNS original guardado para este adaptador.");
        }

        DnsBackupState? original;
        try
        {
            original = JsonSerializer.Deserialize<DnsBackupState>(backupJson);
        }
        catch
        {
            return OperationResult.Fail("Rollback DNS", "Error al leer el archivo de respaldo de DNS.");
        }

        if (original == null)
        {
            return OperationResult.Fail("Rollback DNS", "El estado DNS original respaldado es inválido.");
        }

        OperationResult result;
        if (original.WasDhcp || original.DnsServers == null || original.DnsServers.Count == 0)
        {
            result = await RestoreDhcpDnsAsync(adapterName);
        }
        else
        {
            var preset = new DnsServerPreset
            {
                ProviderName = "DNS Original",
                PrimaryDns = original.DnsServers[0],
                SecondaryDns = original.DnsServers.Count > 1 ? original.DnsServers[1] : string.Empty
            };
            result = await ApplyDnsPresetAsync(adapterName, preset);
        }

        if (result.Success)
        {
            BackupStore.ClearOriginalState(backupKey);
            AppLogger.Success("Rollback DNS", $"Configuración DNS original restaurada en '{adapterName}'.");
        }

        return result;
    }

    public bool HasRollback(string adapterName)
    {
        return !string.IsNullOrEmpty(BackupStore.GetOriginalState($"dns_backup_{adapterName}"));
    }
}
