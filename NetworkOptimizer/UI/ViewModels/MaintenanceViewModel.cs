using NetworkOptimizer.Core;
using NetworkOptimizer.Licensing;
using NetworkOptimizer.Maintenance;

namespace NetworkOptimizer.UI.ViewModels;

public class MaintenanceViewModel : ObservableObject
{
    private readonly NetworkCleanupService _cleanupService = new();
    private readonly LicenseService _licenseService;
    private bool _isBusy;
    private string _lastActionStatus = "Selecciona una tarea de mantenimiento.";

    public bool IsLicensed => _licenseService.IsLicensed;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string LastActionStatus
    {
        get => _lastActionStatus;
        set => SetField(ref _lastActionStatus, value);
    }

    public AsyncRelayCommand FlushDnsCommand { get; }
    public AsyncRelayCommand FlushArpCommand { get; }
    public AsyncRelayCommand RenewDhcpCommand { get; }

    public MaintenanceViewModel() : this(new LicenseService())
    {
    }

    public MaintenanceViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;

        FlushDnsCommand = new AsyncRelayCommand(FlushDnsAsync, () => !IsBusy && _licenseService.IsLicensed);
        FlushArpCommand = new AsyncRelayCommand(FlushArpAsync, () => !IsBusy && _licenseService.IsLicensed);
        RenewDhcpCommand = new AsyncRelayCommand(RenewDhcpAsync, () => !IsBusy && _licenseService.IsLicensed);

        _licenseService.LicenseStateChanged += _ =>
        {
            OnPropertyChanged(nameof(IsLicensed));
            FlushDnsCommand.RaiseCanExecuteChanged();
            FlushArpCommand.RaiseCanExecuteChanged();
            RenewDhcpCommand.RaiseCanExecuteChanged();
        };
    }

    public async Task FlushDnsAsync()
    {
        if (!_licenseService.EnsureLicensed("Vaciado de Caché DNS (Flush DNS)"))
        {
            LastActionStatus = "Se requiere una licencia activa para realizar tareas de mantenimiento.";
            return;
        }

        IsBusy = true;
        LastActionStatus = "Vaciando caché DNS del sistema...";
        try
        {
            var result = await _cleanupService.FlushDnsAsync();
            LastActionStatus = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task FlushArpAsync()
    {
        if (!_licenseService.EnsureLicensed("Vaciado de Tabla ARP"))
        {
            LastActionStatus = "Se requiere una licencia activa para realizar tareas de mantenimiento.";
            return;
        }

        IsBusy = true;
        LastActionStatus = "Vaciando tabla de resolución ARP...";
        try
        {
            var result = await _cleanupService.FlushArpAsync();
            LastActionStatus = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenewDhcpAsync()
    {
        if (!_licenseService.EnsureLicensed("Renovación de Concesión DHCP"))
        {
            LastActionStatus = "Se requiere una licencia activa para realizar tareas de mantenimiento.";
            return;
        }

        IsBusy = true;
        LastActionStatus = "Renovando dirección IP con el router (breve pausa de red)...";
        try
        {
            var result = await _cleanupService.RenewDhcpLeaseAsync();
            LastActionStatus = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
