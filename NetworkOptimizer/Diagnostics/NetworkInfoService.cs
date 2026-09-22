using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics.Models;

namespace NetworkOptimizer.Diagnostics;

public class NetworkInfoService
{
    public List<AdapterInfo> GetAllAdapters()
    {
        var list = new List<AdapterInfo>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var nic in interfaces)
            {
                // Omitir adaptadores de loopback y túneles virtuales temporales si están desconectados
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = nic.GetIPProperties();
                var ipv4Props = nic.Supports(NetworkInterfaceComponent.IPv4) ? ipProps.GetIPv4Properties() : null;

                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "No asignada";

                var ipv6 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)?.Address.ToString() ?? "No asignada";

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString()
                    ?? ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString()
                    ?? "No detectada";

                var dnsServers = ipProps.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList();

                var speedBps = nic.Speed;
                var speedDisplay = FormatSpeed(speedBps);

                int mtu = 0;
                try
                {
                    mtu = ipv4Props?.Mtu ?? 0;
                }
                catch { }

                var isDhcp = false;
                try
                {
                    isDhcp = ipv4Props?.IsDhcpEnabled ?? false;
                }
                catch { }

                var adapter = new AdapterInfo
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    OperationalStatus = nic.OperationalStatus.ToString(),
                    SpeedBitsPerSecond = speedBps,
                    SpeedDisplay = speedDisplay,
                    MacAddress = FormatMac(nic.GetPhysicalAddress().ToString()),
                    Ipv4Address = ipv4,
                    Ipv6Address = ipv6,
                    GatewayAddress = gateway,
                    DnsServers = dnsServers,
                    IsDhcpEnabled = isDhcp,
                    Mtu = mtu,
                    IsActive = nic.OperationalStatus == OperationalStatus.Up && ipv4 != "No asignada"
                };

                list.Add(adapter);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("NetworkInfoService", "Error al escanear interfaces de red", ex.Message);
        }

        return list;
    }

    public AdapterInfo? GetPrimaryActiveAdapter()
    {
        var adapters = GetAllAdapters();

        // Priorizar adaptadores activos con Gateway detectada
        return adapters.FirstOrDefault(a => a.IsActive && a.GatewayAddress != "No detectada" && a.InterfaceType != "Tunnel")
            ?? adapters.FirstOrDefault(a => a.IsActive)
            ?? adapters.FirstOrDefault();
    }

    private static string FormatSpeed(long speedBps)
    {
        if (speedBps <= 0) return "Desconocida / Desconectado";
        if (speedBps >= 1_000_000_000_000) return $"{speedBps / 1_000_000_000_000.0:F1} Tbps";
        if (speedBps >= 1_000_000_000) return $"{speedBps / 1_000_000_000.0:F1} Gbps";
        if (speedBps >= 1_000_000) return $"{speedBps / 1_000_000.0:F1} Mbps";
        if (speedBps >= 1_000) return $"{speedBps / 1_000.0:F1} Kbps";
        return $"{speedBps} bps";
    }

    private static string FormatMac(string mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 12) return mac;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
    }
}
