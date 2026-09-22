using System.Net.Http;
using NetworkOptimizer.Config;
using NetworkOptimizer.Core;

namespace NetworkOptimizer.Diagnostics;

public class PublicIpService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public async Task<string?> GetPublicIpAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var response = await HttpClient.GetStringAsync(AppConfig.PublicIpEndpoint, cts.Token);
            var ip = response.Trim();

            if (System.Net.IPAddress.TryParse(ip, out _))
            {
                return ip;
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PublicIpService", $"No se pudo obtener IP pública (opcional): {ex.Message}");
            return null; // Fallo silencioso y controlado, la aplicación continúa sin problemas
        }
    }
}
