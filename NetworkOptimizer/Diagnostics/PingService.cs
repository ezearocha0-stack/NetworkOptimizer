using System.Net.NetworkInformation;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics.Models;

namespace NetworkOptimizer.Diagnostics;

public class PingService
{
    private static readonly byte[] Buffer = new byte[32]; // Estándar de 32 bytes de payload

    public static List<PingTarget> GetDefaultTargets(string? gatewayIp = null)
    {
        var list = new List<PingTarget>();

        if (!string.IsNullOrWhiteSpace(gatewayIp) && gatewayIp != "0.0.0.0")
        {
            list.Add(new PingTarget("Router / Gateway Local", gatewayIp, "Puerta de enlace predeterminada en tu red local (LAN)"));
        }

        list.Add(new PingTarget("Cloudflare Primary", "1.1.1.1", "Red anycast global ultra rápida"));
        list.Add(new PingTarget("Google Primary", "8.8.8.8", "Servidor global de Google"));
        list.Add(new PingTarget("Quad9 Anycast", "9.9.9.9", "Servidor seguro global Quad9"));
        list.Add(new PingTarget("OpenDNS (Cisco)", "208.67.222.222", "Red anycast de Cisco Umbrella"));

        return list;
    }

    public async Task<PingResult> RunPingTestAsync(
        string targetName,
        string hostOrIp,
        int packetCount = 4,
        int timeoutMs = 1200,
        CancellationToken ct = default)
    {
        var result = new PingResult
        {
            TargetName = targetName,
            HostOrIp = hostOrIp,
            SentPackets = packetCount
        };

        using var ping = new Ping();
        var roundtrips = new List<long>();
        int received = 0;

        for (int i = 0; i < packetCount; i++)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var reply = await ping.SendPingAsync(hostOrIp, timeoutMs, Buffer);
                if (reply.Status == IPStatus.Success)
                {
                    received++;
                    roundtrips.Add(reply.RoundtripTime);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PingService", $"Fallo ping a {hostOrIp} (paquete {i + 1}): {ex.Message}");
            }

            if (i < packetCount - 1)
            {
                await Task.Delay(100, ct); // Pequeña pausa entre paquetes
            }
        }

        result.ReceivedPackets = received;
        result.PacketLossPercent = packetCount > 0 ? ((packetCount - received) / (double)packetCount) * 100.0 : 0;

        if (roundtrips.Count > 0)
        {
            result.Success = true;
            result.MinLatencyMs = roundtrips.Min();
            result.MaxLatencyMs = roundtrips.Max();
            result.AvgLatencyMs = (long)roundtrips.Average();

            // Cálculo de Jitter (variación media entre paquetes sucesivos)
            if (roundtrips.Count > 1)
            {
                double totalDiff = 0;
                for (int i = 1; i < roundtrips.Count; i++)
                {
                    totalDiff += Math.Abs(roundtrips[i] - roundtrips[i - 1]);
                }
                result.JitterMs = totalDiff / (roundtrips.Count - 1);
            }
            else
            {
                result.JitterMs = 0;
            }

            result.StatusMessage = $"Respondido: {received}/{packetCount} paquetes | Mín: {result.MinLatencyMs}ms, Med: {result.AvgLatencyMs}ms, Máx: {result.MaxLatencyMs}ms";
        }
        else
        {
            result.Success = false;
            result.StatusMessage = "Sin respuesta (100% de pérdida de paquetes o destino inalcanzable)";
        }

        return result;
    }
}
