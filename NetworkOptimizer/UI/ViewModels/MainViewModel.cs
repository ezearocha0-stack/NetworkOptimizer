using NetworkOptimizer.Core;
using NetworkOptimizer.Infrastructure;
using NetworkOptimizer.Licensing;

namespace NetworkOptimizer.UI.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly LicenseService _licenseService;
    private int _selectedTabIndex;
    private string _statusMessage = "Listo. Ejecutando como usuario estándar.";

    public LicenseService LicenseService => _licenseService;

    public DashboardViewModel Dashboard { get; }
    public DnsViewModel Dns { get; }
    public OptimizationsViewModel Optimizations { get; }
    public MaintenanceViewModel Maintenance { get; }
    public LicensingViewModel Licensing { get; }
    public LogsViewModel Logs { get; } = new();
    public UpdateViewModel Update { get; } = new();

    public bool IsElevated => AdminElevationHelper.IsElevated;
    public string ElevationStatusText => IsElevated
        ? "Modo Administrador (Elevado)"
        : "Modo Estándar (Sin elevación)";

    public bool IsLicensed => _licenseService.IsLicensed;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public RelayCommand RequestElevationCommand { get; }
    public RelayCommand NavigateToLicenseCommand { get; }

    public MainViewModel() : this(new LicenseService())
    {
    }

    public MainViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;

        Dashboard = new DashboardViewModel(_licenseService);
        Dns = new DnsViewModel(_licenseService);
        Optimizations = new OptimizationsViewModel(_licenseService);
        Maintenance = new MaintenanceViewModel(_licenseService);
        Licensing = new LicensingViewModel(_licenseService);

        _licenseService.LicenseStateChanged += _ =>
        {
            OnPropertyChanged(nameof(IsLicensed));
        };

        RequestElevationCommand = new RelayCommand(RequestElevation, () => !IsElevated);
        NavigateToLicenseCommand = new RelayCommand(() => SelectedTabIndex = 4);

        if (IsElevated)
        {
            _statusMessage = "Listo. Privilegios de administrador activos.";
        }
    }

    private void RequestElevation()
    {
        AppLogger.Info("Sistema", "Usuario solicitó reiniciar aplicación con privilegios de Administrador completos...");
        AdminElevationHelper.RestartElevated();
    }
}
