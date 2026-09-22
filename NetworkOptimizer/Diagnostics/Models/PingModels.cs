namespace NetworkOptimizer.Diagnostics.Models;

public record PingTarget(string Name, string HostOrIp, string Description, bool IsCustom = false);

public class PingResult
{
    public string TargetName { get; set; } = string.Empty;
    public string HostOrIp { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long MinLatencyMs { get; set; }
    public long AvgLatencyMs { get; set; }
    public long MaxLatencyMs { get; set; }
    public double JitterMs { get; set; }
    public double PacketLossPercent { get; set; }
    public int SentPackets { get; set; }
    public int ReceivedPackets { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
