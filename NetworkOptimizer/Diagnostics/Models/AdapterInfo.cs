namespace NetworkOptimizer.Diagnostics.Models;

public class AdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string OperationalStatus { get; set; } = string.Empty;
    public string SpeedDisplay { get; set; } = string.Empty;
    public long SpeedBitsPerSecond { get; set; }
    public string MacAddress { get; set; } = string.Empty;
    public string Ipv4Address { get; set; } = string.Empty;
    public string Ipv6Address { get; set; } = string.Empty;
    public string GatewayAddress { get; set; } = string.Empty;
    public List<string> DnsServers { get; set; } = new();
    public string DnsServersDisplay => DnsServers.Count > 0 ? string.Join(", ", DnsServers) : "Automático / DHCP";
    public bool IsDhcpEnabled { get; set; }
    public int Mtu { get; set; }
    public bool IsActive { get; set; }
}
