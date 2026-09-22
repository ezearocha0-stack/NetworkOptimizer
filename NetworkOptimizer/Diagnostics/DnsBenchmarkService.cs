using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics.Models;

namespace NetworkOptimizer.Diagnostics;

public class DnsBenchmarkService
{
    private static readonly string[] TestDomains = new[]
    {
        "google.com",
        "cloudflare.com",
        "microsoft.com"
    };

    public static List<DnsServerPreset> GetPresets(List<string>? currentDns = null)
    {
        var list = new List<DnsServerPreset>();

        if (currentDns != null && currentDns.Count > 0)
        {
            list.Add(new DnsServerPreset
            {
                ProviderName = "DNS Actual",
                Description = "Servidores configurados actualmente en tu adaptador",
                PrimaryDns = currentDns[0],
                SecondaryDns = currentDns.Count > 1 ? currentDns[1] : string.Empty
            });
        }

        list.Add(new DnsServerPreset
        {
            ProviderName = "Cloudflare",
            Description = "Enfocado en velocidad extrema y privacidad (sin registro de IP)",
            PrimaryDns = "1.1.1.1",
            SecondaryDns = "1.0.0.1"
        });

        list.Add(new DnsServerPreset
        {
            ProviderName = "Google Public DNS",
            Description = "Red global anycast de Google con altísima disponibilidad",
            PrimaryDns = "8.8.8.8",
            SecondaryDns = "8.8.4.4"
        });

        list.Add(new DnsServerPreset
        {
            ProviderName = "Quad9",
            Description = "Seguridad integrada contra malware, phishing y telemetría no deseada",
            PrimaryDns = "9.9.9.9",
            SecondaryDns = "149.112.112.112"
        });

        list.Add(new DnsServerPreset
        {
            ProviderName = "OpenDNS (Cisco)",
            Description = "Excelente fiabilidad e infraestructura empresarial de Cisco",
            PrimaryDns = "208.67.222.222",
            SecondaryDns = "208.67.220.220"
        });

        return list;
    }

    public async Task<List<DnsBenchmarkResult>> BenchmarkAllPresetsAsync(
        List<DnsServerPreset> presets,
        int timeoutMs = 1500,
        CancellationToken ct = default)
    {
        var results = new List<DnsBenchmarkResult>();

        foreach (var preset in presets)
        {
            if (ct.IsCancellationRequested) break;

            var primaryResult = await TestDnsServerAsync(preset.ProviderName, preset.PrimaryDns, preset.Description, timeoutMs, ct);
            results.Add(primaryResult);
        }

        // Ordenar por disponibilidad y luego por tiempo de respuesta más bajo
        return results
            .OrderByDescending(r => r.IsAvailable)
            .ThenBy(r => r.ResponseTimeMs)
            .ToList();
    }

    public async Task<DnsBenchmarkResult> TestDnsServerAsync(
        string providerName,
        string serverIp,
        string description,
        int timeoutMs = 1500,
        CancellationToken ct = default)
    {
        var result = new DnsBenchmarkResult
        {
            ProviderName = providerName,
            ServerIp = serverIp,
            Description = description,
            TotalQueries = TestDomains.Length
        };

        if (!IPAddress.TryParse(serverIp, out var serverAddress))
        {
            result.IsAvailable = false;
            result.StatusMessage = "Dirección IP inválida";
            return result;
        }

        var latencies = new List<double>();
        int successCount = 0;

        foreach (var domain in TestDomains)
        {
            if (ct.IsCancellationRequested) break;

            var latency = await QueryDnsDirectAsync(serverAddress, domain, timeoutMs, ct);
            if (latency.HasValue)
            {
                successCount++;
                latencies.Add(latency.Value);
            }
        }

        result.SuccessfulQueries = successCount;
        if (successCount > 0)
        {
            result.IsAvailable = true;
            result.ResponseTimeMs = latencies.Average();
            result.StatusMessage = $"{successCount}/{TestDomains.Length} dominios resueltos correctamente";
        }
        else
        {
            result.IsAvailable = false;
            result.ResponseTimeMs = 9999;
            result.StatusMessage = "Sin respuesta (timeout en UDP 53)";
        }

        return result;
    }

    /// <summary>
    /// Envía un paquete de consulta DNS RFC 1035 crudo sobre UDP 53 al servidor especificado
    /// para medir con precisión el RTT real de resolución evitando la caché de Windows.
    /// </summary>
    private static async Task<double?> QueryDnsDirectAsync(
        IPAddress serverAddress,
        string domain,
        int timeoutMs,
        CancellationToken ct)
    {
        try
        {
            using var client = new UdpClient();
            client.Client.SendTimeout = timeoutMs;
            client.Client.ReceiveTimeout = timeoutMs;

            var queryPacket = BuildDnsQuery(domain);
            var endpoint = new IPEndPoint(serverAddress, 53);

            var sw = Stopwatch.StartNew();
            await client.SendAsync(queryPacket, queryPacket.Length, endpoint);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var receiveTask = client.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, cts.Token));

            if (completedTask == receiveTask)
            {
                sw.Stop();
                var response = await receiveTask;
                if (response.Buffer.Length >= 12) // Cabecera DNS mínima
                {
                    return sw.Elapsed.TotalMilliseconds;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildDnsQuery(string domain)
    {
        var packet = new List<byte>();

        // ID de transacción aleatorio
        var random = new Random();
        var id = (ushort)random.Next(0, 65535);
        packet.Add((byte)(id >> 8));
        packet.Add((byte)(id & 0xFF));

        // Flags: Standard query, recursion desired (0x0100)
        packet.Add(0x01);
        packet.Add(0x00);

        // Questions: 1
        packet.Add(0x00);
        packet.Add(0x01);

        // Answer RRs: 0
        packet.Add(0x00);
        packet.Add(0x00);

        // Authority RRs: 0
        packet.Add(0x00);
        packet.Add(0x00);

        // Additional RRs: 0
        packet.Add(0x00);
        packet.Add(0x00);

        // QNAME: etiquetas separadas por longitud (ej: 6 google 3 com 0)
        var parts = domain.Split('.');
        foreach (var part in parts)
        {
            packet.Add((byte)part.Length);
            foreach (var ch in part)
            {
                packet.Add((byte)ch);
            }
        }
        packet.Add(0x00); // Terminador nulo

        // QTYPE: 0x0001 (Tipo A)
        packet.Add(0x00);
        packet.Add(0x01);

        // QCLASS: 0x0001 (Clase IN - Internet)
        packet.Add(0x00);
        packet.Add(0x01);

        return packet.ToArray();
    }
}
