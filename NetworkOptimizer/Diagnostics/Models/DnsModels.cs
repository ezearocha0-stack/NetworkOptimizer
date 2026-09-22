namespace NetworkOptimizer.Diagnostics.Models;

public class DnsServerPreset
{
    public string ProviderName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryDns { get; set; } = string.Empty;
    public string SecondaryDns { get; set; } = string.Empty;
    public bool IsDhcp { get; set; }
}

public class DnsBenchmarkResult
{
    public string ProviderName { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public double ResponseTimeMs { get; set; }
    public int SuccessfulQueries { get; set; }
    public int TotalQueries { get; set; }
    public string StatusMessage { get; set; } = string.Empty;

    public string ResponseTimeDisplay => IsAvailable ? $"{ResponseTimeMs:F1} ms" : "No disponible / Timeout";
}
