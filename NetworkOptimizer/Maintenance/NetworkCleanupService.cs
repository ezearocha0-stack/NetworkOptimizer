using System.Runtime.InteropServices;
using NetworkOptimizer.Core;
using NetworkOptimizer.Infrastructure;

namespace NetworkOptimizer.Maintenance;

public class NetworkCleanupService
{
    // API nativa de Windows en dnsapi.dll para limpiar la caché DNS al instante sin CMD
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern bool DnsFlushResolverCache();

    /// <summary>
    /// Limpia la caché local de resolución DNS.
    /// Operación 100% segura, instantánea y no requiere permisos de administrador.
    /// </summary>
    public async Task<OperationResult> FlushDnsAsync()
    {
        try
        {
            AppLogger.Info("Mantenimiento", "Iniciando limpieza de caché DNS...");

            bool success = false;
            try
            {
                success = DnsFlushResolverCache();
            }
            catch
            {
                // Fallback a ipconfig /flushdns si la P/Invoke tuviese algún problema
                var (runSuccess, exitCode, _, _) = await ProcessRunner.RunAsync("ipconfig", "/flushdns", 5000);
                success = runSuccess && exitCode == 0;
            }

            if (success)
            {
                var result = OperationResult.Ok("Flush DNS", "Caché de resolución DNS de Windows vaciada correctamente.");
                AppLogger.LogResult(result);
                return result;
            }
            else
            {
                var result = OperationResult.Fail("Flush DNS", "No se pudo vaciar la caché DNS.", "Error al invocar DnsFlushResolverCache.");
                AppLogger.LogResult(result);
                return result;
            }
        }
        catch (Exception ex)
        {
            var result = OperationResult.Fail("Flush DNS", "Excepción al vaciar caché DNS.", ex.Message);
            AppLogger.LogResult(result);
            return result;
        }
    }

    /// <summary>
    /// Limpia la tabla de resolución ARP de la red local.
    /// Requiere elevación de Administrador.
    /// </summary>
    public async Task<OperationResult> FlushArpAsync()
    {
        AppLogger.Info("Mantenimiento", "Iniciando limpieza de caché ARP...");

        if (!AdminElevationHelper.IsElevated)
        {
            // Ejecutar elevando vía UAC
            var (elevatedSuccess, exitCode, _, error) = await AdminElevationHelper.RunElevatedAsync(
                "netsh.exe",
                "interface ip delete arpcache");

            if (elevatedSuccess)
            {
                var result = OperationResult.Ok("Flush ARP", "Tabla de caché ARP de la red local vaciada con éxito mediante elevación UAC.");
                AppLogger.LogResult(result);
                return result;
            }
            else
            {
                var result = OperationResult.Fail("Flush ARP", "No se pudo vaciar la caché ARP.", error);
                AppLogger.LogResult(result);
                return result;
            }
        }

        var (success, code, output, err) = await ProcessRunner.RunAsync("netsh", "interface ip delete arpcache", 5000);
        if (success && code == 0)
        {
            var result = OperationResult.Ok("Flush ARP", "Tabla de caché ARP de la red local vaciada con éxito.");
            AppLogger.LogResult(result);
            return result;
        }
        else
        {
            var result = OperationResult.Fail("Flush ARP", "Fallo al ejecutar vaciado ARP.", err);
            AppLogger.LogResult(result);
            return result;
        }
    }

    /// <summary>
    /// Solicita una nueva concesión de dirección IP al servidor DHCP (router).
    /// ADVERTENCIA: Provoca una breve desconexión de red de 1 a 3 segundos.
    /// </summary>
    public async Task<OperationResult> RenewDhcpLeaseAsync()
    {
        AppLogger.Info("Mantenimiento", "Solicitando renovación de concesión DHCP al router...");

        if (!AdminElevationHelper.IsElevated)
        {
            var (elevatedSuccess, exitCode, _, error) = await AdminElevationHelper.RunElevatedAsync(
                "ipconfig.exe",
                "/renew");

            if (elevatedSuccess)
            {
                var result = OperationResult.Ok("Renovar DHCP", "Concesión DHCP renovada con éxito tras reconexión.");
                AppLogger.LogResult(result);
                return result;
            }
            else
            {
                var result = OperationResult.Fail("Renovar DHCP", "No se pudo renovar la concesión DHCP.", error);
                AppLogger.LogResult(result);
                return result;
            }
        }

        var (success, code, output, err) = await ProcessRunner.RunAsync("ipconfig", "/renew", 15000);
        if (success && code == 0)
        {
            var result = OperationResult.Ok("Renovar DHCP", "Concesión DHCP renovada con éxito.");
            AppLogger.LogResult(result);
            return result;
        }
        else
        {
            var result = OperationResult.Fail("Renovar DHCP", "Fallo al renovar DHCP.", err);
            AppLogger.LogResult(result);
            return result;
        }
    }
}
