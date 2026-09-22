namespace NetworkOptimizer.Config;

/// <summary>
/// Configuración centralizada de NetworkOptimizer.
/// Evita la dispersión de constantes y slugs por el código.
/// </summary>
public static class AppConfig
{
    public const string ProductName = "NetworkOptimizer";
    public const string ProductSlug = "network-optimizer";
    public const string AppVersion = "1.0.0";

    /// <summary>
    /// URL base del servidor oficial de licencias.
    /// </summary>
    public const string LicenseApiBaseUrl = "https://gestor-claves-backend.onrender.com";

    /// <summary>
    /// Endpoint para detección opcional de IP pública con timeout corto.
    /// </summary>
    public const string PublicIpEndpoint = "https://api.ipify.org";

    /// <summary>
    /// Timeout en segundos para peticiones de licencias.
    /// </summary>
    public const int LicenseTimeoutSeconds = 10;

    /// <summary>
    /// URL remota del manifiesto de actualización (latest.json).
    /// PLACEHOLDER: Reemplazar con la URL HTTPS de distribución en producción (ej. GitHub Releases, CDN, AWS S3).
    /// </summary>
    public const string UpdateManifestUrl = "https://tu-hosting.com/NetworkOptimizer/latest.json";

    /// <summary>
    /// Nombre del archivo ejecutable del actualizador auxiliar.
    /// </summary>
    public const string UpdaterExecutableName = "NetworkOptimizer.Updater.exe";
}
