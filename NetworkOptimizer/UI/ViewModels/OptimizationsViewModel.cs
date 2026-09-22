using NetworkOptimizer.Core;
using NetworkOptimizer.Licensing;
using NetworkOptimizer.Optimizations;

namespace NetworkOptimizer.UI.ViewModels;

public class OptimizationsViewModel : ObservableObject
{
    private readonly TcpAutoTuningOptimization _tcpOptimization = new();
    private readonly RssOptimization _rssOptimization = new();
    private readonly LicenseService _licenseService;

    private string _tcpState = "Consultando...";
    private string _rssState = "Consultando...";
    private bool _isBusy;
    private string _statusNotice = "Listo para verificar o aplicar optimizaciones seguras.";

    public TcpAutoTuningOptimization TcpOptimization => _tcpOptimization;
    public RssOptimization RssOptimization => _rssOptimization;

    public bool IsLicensed => _licenseService.IsLicensed;

    public string TcpState
    {
        get => _tcpState;
        set => SetField(ref _tcpState, value);
    }

    public string RssState
    {
        get => _rssState;
        set => SetField(ref _rssState, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string StatusNotice
    {
        get => _statusNotice;
        set => SetField(ref _statusNotice, value);
    }

    public bool CanRollbackTcp => _tcpOptimization.CanRollback;
    public bool CanRollbackRss => _rssOptimization.CanRollback;

    public AsyncRelayCommand RefreshStatesCommand { get; }
    public AsyncRelayCommand ApplyTcpCommand { get; }
    public AsyncRelayCommand RollbackTcpCommand { get; }
    public AsyncRelayCommand ApplyRssCommand { get; }
    public AsyncRelayCommand RollbackRssCommand { get; }

    public OptimizationsViewModel() : this(new LicenseService())
    {
    }

    public OptimizationsViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;

        RefreshStatesCommand = new AsyncRelayCommand(RefreshAllStatesAsync, () => !IsBusy);
        ApplyTcpCommand = new AsyncRelayCommand(ApplyTcpAsync, () => !IsBusy && _licenseService.IsLicensed);
        RollbackTcpCommand = new AsyncRelayCommand(RollbackTcpAsync, () => !IsBusy && CanRollbackTcp && _licenseService.IsLicensed);
        ApplyRssCommand = new AsyncRelayCommand(ApplyRssAsync, () => !IsBusy && _rssOptimization.IsSupported && _licenseService.IsLicensed);
        RollbackRssCommand = new AsyncRelayCommand(RollbackRssAsync, () => !IsBusy && CanRollbackRss && _licenseService.IsLicensed);

        _licenseService.LicenseStateChanged += _ =>
        {
            OnPropertyChanged(nameof(IsLicensed));
            ApplyTcpCommand.RaiseCanExecuteChanged();
            RollbackTcpCommand.RaiseCanExecuteChanged();
            ApplyRssCommand.RaiseCanExecuteChanged();
            RollbackRssCommand.RaiseCanExecuteChanged();
        };

        _ = RefreshAllStatesAsync();
    }

    public async Task RefreshAllStatesAsync()
    {
        IsBusy = true;
        StatusNotice = "Comprobando configuración actual en Windows...";
        try
        {
            TcpState = await _tcpOptimization.RefreshCurrentStateAsync();
            RssState = await _rssOptimization.RefreshCurrentStateAsync();

            OnPropertyChanged(nameof(CanRollbackTcp));
            OnPropertyChanged(nameof(CanRollbackRss));
            StatusNotice = "Estados actuales actualizados correctamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyTcpAsync()
    {
        if (!_licenseService.EnsureLicensed("TCP Window Auto-Tuning"))
        {
            StatusNotice = "Se requiere una licencia activa para modificar parámetros TCP.";
            return;
        }

        IsBusy = true;
        StatusNotice = "Aplicando nivel Normal en TCP Window Auto-Tuning...";
        try
        {
            var result = await _tcpOptimization.ApplyAsync();
            StatusNotice = result.Message;
            TcpState = _tcpOptimization.CurrentState ?? "Actualizado";
            OnPropertyChanged(nameof(CanRollbackTcp));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RollbackTcpAsync()
    {
        if (!_licenseService.EnsureLicensed("Rollback de TCP Auto-Tuning"))
        {
            StatusNotice = "Se requiere una licencia activa para revertir parámetros TCP.";
            return;
        }

        IsBusy = true;
        StatusNotice = "Revirtiendo TCP Auto-Tuning al estado original...";
        try
        {
            var result = await _tcpOptimization.RollbackAsync();
            StatusNotice = result.Message;
            TcpState = _tcpOptimization.CurrentState ?? "Revertido";
            OnPropertyChanged(nameof(CanRollbackTcp));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyRssAsync()
    {
        if (!_licenseService.EnsureLicensed("Habilitación de Receive-Side Scaling (RSS)"))
        {
            StatusNotice = "Se requiere una licencia activa para modificar la configuración de red.";
            return;
        }

        IsBusy = true;
        StatusNotice = "Habilitando Receive-Side Scaling (RSS)...";
        try
        {
            var result = await _rssOptimization.ApplyAsync();
            StatusNotice = result.Message;
            RssState = _rssOptimization.CurrentState ?? "Actualizado";
            OnPropertyChanged(nameof(CanRollbackRss));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RollbackRssAsync()
    {
        if (!_licenseService.EnsureLicensed("Rollback de Receive-Side Scaling (RSS)"))
        {
            StatusNotice = "Se requiere una licencia activa para revertir la configuración de red.";
            return;
        }

        IsBusy = true;
        StatusNotice = "Revirtiendo RSS al estado original...";
        try
        {
            var result = await _rssOptimization.RollbackAsync();
            StatusNotice = result.Message;
            RssState = _rssOptimization.CurrentState ?? "Revertido";
            OnPropertyChanged(nameof(CanRollbackRss));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
