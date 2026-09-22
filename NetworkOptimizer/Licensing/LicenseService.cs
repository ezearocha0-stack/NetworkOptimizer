using Licensing.Client.Configuration;
using Licensing.Client.Licensing;
using Licensing.Client.Models;
using Licensing.Client.Resilience;
using NetworkOptimizer.Config;
using NetworkOptimizer.Core;

namespace NetworkOptimizer.Licensing;

public record LicenseStatusInfo(
    bool IsLicensed,
    string StatusText,
    string? LicenseType,
    string? ExpirationDate,
    string? HardwareId);

public class LicenseService
{
    private readonly ILicenseClient _client;
    private readonly ClientConfig _config;
    private bool _isLicensed;

    /// <summary>
    /// Indica si el producto cuenta actualmente con una autorización válida de licencia.
    /// </summary>
    public bool IsLicensed
    {
        get => _isLicensed;
        private set
        {
            if (_isLicensed != value)
            {
                _isLicensed = value;
                NotifyLicenseStateChanged(value);
            }
        }
    }

    private void NotifyLicenseStateChanged(bool isLicensed)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => LicenseStateChanged?.Invoke(isLicensed));
        }
        else
        {
            LicenseStateChanged?.Invoke(isLicensed);
        }
    }

    /// <summary>
    /// Evento emitido cuando cambia el estado de autorización de la licencia.
    /// </summary>
    public event Action<bool>? LicenseStateChanged;

    public LicenseService()
    {
        _config = new ClientConfig
        {
            ProductSlug = AppConfig.ProductSlug,
            ProductName = AppConfig.ProductName,
            ClientVersion = AppConfig.AppVersion,
            ApiBaseUrl = AppConfig.LicenseApiBaseUrl,
            RequestTimeoutSeconds = AppConfig.LicenseTimeoutSeconds
        };
        _config.Validate();

        _client = new LicenseClient(_config);

        // Inicializar estado de autorización a partir de la caché protegida local
        var initialStatus = GetCachedStatus();
        _isLicensed = initialStatus.IsLicensed;
    }

    // Constructor para inyección de dependencias y pruebas unitarias
    public LicenseService(ILicenseClient client, ClientConfig config)
    {
        _client = client;
        _config = config;

        var initialStatus = GetCachedStatus();
        _isLicensed = initialStatus.IsLicensed;
    }

    /// <summary>
    /// Validación centralizada para verificar si una función protegida puede ejecutarse.
    /// Registra advertencia sanitizada en caso de acceso denegado.
    /// </summary>
    public bool EnsureLicensed(string featureName)
    {
        if (!IsLicensed)
        {
            AppLogger.Warn("Licensing", $"Acceso denegado a [{featureName}]: se requiere una licencia activa del producto.");
            return false;
        }

        return true;
    }

    public string GetHardwareId()
    {
        try
        {
            return _client.GetDeviceFingerprint();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Licensing", $"Error al obtener HWID: {ex.Message}");
            return "No disponible";
        }
    }

    public LicenseStatusInfo GetCachedStatus()
    {
        try
        {
            var cached = _client.GetCachedLicense();
            if (cached != null)
            {
                // 1. Verificar si la licencia está expirada según la fecha almacenada
                if (cached.ExpiresAt.HasValue && cached.ExpiresAt.Value < DateTime.UtcNow)
                {
                    IsLicensed = false;
                    return new LicenseStatusInfo(
                        IsLicensed: false,
                        StatusText: "Licencia expirada",
                        LicenseType: cached.ProductName,
                        ExpirationDate: cached.ExpiresAt?.ToString("yyyy-MM-dd HH:mm"),
                        HardwareId: GetHardwareId()
                    );
                }

                // 2. Verificar si el estado revocado o suspendido fue registrado
                if (string.Equals(cached.LastKnownStatus, "Revoked", StringComparison.OrdinalIgnoreCase))
                {
                    IsLicensed = false;
                    return new LicenseStatusInfo(
                        IsLicensed: false,
                        StatusText: "Licencia revocada",
                        LicenseType: cached.ProductName,
                        ExpirationDate: cached.ExpiresAt?.ToString("yyyy-MM-dd HH:mm"),
                        HardwareId: GetHardwareId()
                    );
                }

                if (string.Equals(cached.LastKnownStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                {
                    IsLicensed = false;
                    return new LicenseStatusInfo(
                        IsLicensed: false,
                        StatusText: "Licencia suspendida",
                        LicenseType: cached.ProductName,
                        ExpirationDate: cached.ExpiresAt?.ToString("yyyy-MM-dd HH:mm"),
                        HardwareId: GetHardwareId()
                    );
                }

                // 3. Verificar si el período de gracia offline expiró (si aplica)
                var maxGraceDuration = GracePeriodOptions.Default.MaxDuration + GracePeriodOptions.Default.ClockSkewTolerance;
                if (cached.LastValidatedUtc != default && (DateTimeOffset.UtcNow - cached.LastValidatedUtc) > maxGraceDuration)
                {
                    IsLicensed = false;
                    return new LicenseStatusInfo(
                        IsLicensed: false,
                        StatusText: "Período de gracia offline expirado",
                        LicenseType: cached.ProductName,
                        ExpirationDate: cached.ExpiresAt?.ToString("yyyy-MM-dd HH:mm"),
                        HardwareId: GetHardwareId()
                    );
                }

                // Licencia en caché válida y vigente
                IsLicensed = true;
                return new LicenseStatusInfo(
                    IsLicensed: true,
                    StatusText: "Licencia activa (Caché local verificada)",
                    LicenseType: cached.ProductName,
                    ExpirationDate: cached.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "Permanente",
                    HardwareId: GetHardwareId()
                );
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Licensing", $"Error al leer licencia en caché: {ex.Message}");
        }

        IsLicensed = false;
        return new LicenseStatusInfo(
            IsLicensed: false,
            StatusText: "No activado / Sin licencia",
            LicenseType: null,
            ExpirationDate: null,
            HardwareId: GetHardwareId()
        );
    }

    public async Task<LicenseStatusInfo> ValidateCurrentLicenseAsync(string? key = null, CancellationToken ct = default)
    {
        try
        {
            AppLogger.Info("Licensing", "Consultando estado de licencia con el servidor oficial...");

            var keyToValidate = key;
            if (string.IsNullOrEmpty(keyToValidate))
            {
                var cached = _client.GetCachedLicense();
                keyToValidate = cached?.LicenseKey;
            }

            if (string.IsNullOrEmpty(keyToValidate))
            {
                var cachedStatus = GetCachedStatus();
                IsLicensed = cachedStatus.IsLicensed;
                return cachedStatus;
            }

            var result = await _client.ValidateAsync(keyToValidate, ct);

            if (result.IsValid)
            {
                AppLogger.Success("Licensing", "Licencia validada exitosamente con el servidor.");
                IsLicensed = true;
                return new LicenseStatusInfo(
                    IsLicensed: true,
                    StatusText: "Licencia Válida",
                    LicenseType: result.Product ?? "Pro",
                    ExpirationDate: result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "Permanente",
                    HardwareId: GetHardwareId()
                );
            }

            // Manejo de tolerancia offline: si el servidor no responde pero existe caché con gracia vigente
            if (result.ValidationOutcome == ValidationOutcome.ServerUnavailable)
            {
                var cachedStatus = GetCachedStatus();
                if (cachedStatus.IsLicensed)
                {
                    AppLogger.Info("Licensing", "Servidor no disponible. Manteniendo acceso offline bajo período de gracia vigente.");
                    IsLicensed = true;
                    return new LicenseStatusInfo(
                        IsLicensed: true,
                        StatusText: "Modo Offline (Período de gracia activo)",
                        LicenseType: cachedStatus.LicenseType,
                        ExpirationDate: cachedStatus.ExpirationDate,
                        HardwareId: GetHardwareId()
                    );
                }

                IsLicensed = false;
                AppLogger.Warn("Licensing", "Servidor no disponible y no existe período de gracia válido.");
                return new LicenseStatusInfo(
                    IsLicensed: false,
                    StatusText: "Servidor no disponible / Sin licencia activa",
                    LicenseType: null,
                    ExpirationDate: null,
                    HardwareId: GetHardwareId()
                );
            }

            // Licencia inválida, revocada o expirada confirmada por el servidor
            IsLicensed = false;
            AppLogger.Warn("Licensing", $"Licencia no autorizada: {result.ValidationOutcome}");
            return new LicenseStatusInfo(
                IsLicensed: false,
                StatusText: $"No válida ({result.ValidationOutcome})",
                LicenseType: null,
                ExpirationDate: null,
                HardwareId: GetHardwareId()
            );
        }
        catch (Exception ex)
        {
            AppLogger.Error("Licensing", "Error al conectar con el servidor de licencias", ex.Message);
            // Si hay error inesperado pero hay caché local válida, recurrir al estado de caché
            var cachedStatus = GetCachedStatus();
            if (cachedStatus.IsLicensed)
            {
                IsLicensed = true;
                return cachedStatus;
            }

            IsLicensed = false;
            return new LicenseStatusInfo(
                IsLicensed: false,
                StatusText: "Error de conexión con servidor de licencias",
                LicenseType: null,
                ExpirationDate: null,
                HardwareId: GetHardwareId()
            );
        }
    }

    public async Task<(bool Success, string Message, LicenseStatusInfo Status)> ActivateKeyAsync(
        string key,
        string? machineName = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, "Debes introducir una clave de licencia.", GetCachedStatus());
            }

            AppLogger.Info("Licensing", "Enviando solicitud de activación al servidor...");

            var name = machineName ?? Environment.MachineName;
            var result = await _client.ActivateAsync(key.Trim(), name, ct);

            if (result.IsValid)
            {
                AppLogger.Success("Licensing", "Activación exitosa. Equipo vinculado en el servidor.");
                IsLicensed = true;
                var status = new LicenseStatusInfo(
                    IsLicensed: true,
                    StatusText: "Activación Exitosa",
                    LicenseType: result.Product ?? "Pro",
                    ExpirationDate: result.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "Permanente",
                    HardwareId: GetHardwareId()
                );
                return (true, "¡Producto activado con éxito!", status);
            }
            else
            {
                IsLicensed = false;
                var msg = $"Fallo de activación: {result.ActivationOutcome}";
                AppLogger.Warn("Licensing", msg);
                return (false, msg, GetCachedStatus());
            }
        }
        catch (Exception ex)
        {
            IsLicensed = false;
            AppLogger.Error("Licensing", "Error durante la activación", ex.Message);
            return (false, $"Error al conectar con el servidor: {ex.Message}", GetCachedStatus());
        }
    }

    public void Deactivate()
    {
        try
        {
            _client.ClearLocalLicense();
            IsLicensed = false;
            NotifyLicenseStateChanged(false);
            AppLogger.Info("Licensing", "Licencia local eliminada. Funciones protegidas bloqueadas.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Licensing", $"Error al limpiar licencia local: {ex.Message}");
        }
    }
}
