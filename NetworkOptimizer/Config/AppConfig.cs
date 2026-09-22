namespace NetworkOptimizer.Config;

/// <summary>
/// Configuración centralizada de NetworkOptimizer.
/// Evita la dispersión de constantes y slugs por el código.
/// </summary>
public static class AppConfig
{
    public const string ProductName = "NetworkOptimizer";
    public const string ProductSlug = "network-optimizer";
    public const string AppVersion = "1.1.0";

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
    /// Propietario y repositorio oficial en GitHub para distribución de releases.
    /// </summary>
    public const string GitHubOwner = "ezearocha0-stack";
    public const string GitHubRepository = "NetworkOptimizer";
    public const string GitHubReleasesUrl = "https://github.com/ezearocha0-stack/NetworkOptimizer/releases";

    /// <summary>
    /// URL remota oficial del manifiesto de actualización (latest.json) en GitHub (sin autenticación requerida).
    /// </summary>
    public const string UpdateManifestUrl = "https://raw.githubusercontent.com/ezearocha0-stack/NetworkOptimizer/main/latest.json";

    /// <summary>
    /// Nombre del archivo ejecutable del actualizador auxiliar.
    /// </summary>
    public const string UpdaterExecutableName = "NetworkOptimizer.Updater.exe";
}
