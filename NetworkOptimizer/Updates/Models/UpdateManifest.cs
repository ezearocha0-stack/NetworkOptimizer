using System.Text.Json.Serialization;

namespace NetworkOptimizer.Updates.Models;

/// <summary>
/// Modelo del manifiesto de actualización remoto (latest.json).
/// </summary>
public record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256);
