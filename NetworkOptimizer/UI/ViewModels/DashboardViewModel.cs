using System.Collections.ObjectModel;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics;
using NetworkOptimizer.Diagnostics.Models;
using NetworkOptimizer.Licensing;

namespace NetworkOptimizer.UI.ViewModels;

public class DashboardViewModel : ObservableObject
{
    private readonly NetworkInfoService _netInfoService = new();
    private readonly PingService _pingService = new();
    private readonly PublicIpService _publicIpService = new();
    private readonly LicenseService _licenseService;

    private AdapterInfo? _selectedAdapter;
    private string _publicIp = "Consultando...";
    private bool _isTestingPing;
    private PingResult? _currentPingResult;
    private string _customTargetHost = string.Empty;
    private PingTarget? _selectedPingTarget;

    public ObservableCollection<AdapterInfo> Adapters { get; } = new();
    public ObservableCollection<PingTarget> PingTargets { get; } = new();
    public ObservableCollection<PingResult> PingHistory { get; } = new();

    public bool IsLicensed => _licenseService.IsLicensed;

    public AdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set => SetField(ref _selectedAdapter, value);
    }

    public string PublicIp
    {
        get => _publicIp;
        set => SetField(ref _publicIp, value);
    }

    public bool IsTestingPing
    {
        get => _isTestingPing;
        set => SetField(ref _isTestingPing, value);
    }

    public PingResult? CurrentPingResult
    {
        get => _currentPingResult;
        set => SetField(ref _currentPingResult, value);
    }

    public string CustomTargetHost
    {
        get => _customTargetHost;
        set => SetField(ref _customTargetHost, value);
    }

    public PingTarget? SelectedPingTarget
    {
        get => _selectedPingTarget;
        set => SetField(ref _selectedPingTarget, value);
    }

    public AsyncRelayCommand RefreshAdaptersCommand { get; }
    public AsyncRelayCommand RunPingCommand { get; }
    public RelayCommand AddCustomTargetCommand { get; }
    public AsyncRelayCommand RefreshPublicIpCommand { get; }

    public DashboardViewModel() : this(new LicenseService())
    {
    }

    public DashboardViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;

        RefreshAdaptersCommand = new AsyncRelayCommand(LoadAdaptersAsync);
        RunPingCommand = new AsyncRelayCommand(RunPingTestAsync, () => !IsTestingPing && SelectedPingTarget != null && _licenseService.IsLicensed);
        AddCustomTargetCommand = new RelayCommand(AddCustomTarget, () => !string.IsNullOrWhiteSpace(CustomTargetHost));
        RefreshPublicIpCommand = new AsyncRelayCommand(LoadPublicIpAsync);

        _licenseService.LicenseStateChanged += _ =>
        {
            OnPropertyChanged(nameof(IsLicensed));
            RunPingCommand.RaiseCanExecuteChanged();
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadAdaptersAsync();
        await LoadPublicIpAsync();
    }

    public async Task LoadAdaptersAsync()
    {
        Adapters.Clear();
        var list = _netInfoService.GetAllAdapters();
        foreach (var adapter in list)
        {
            Adapters.Add(adapter);
        }

        SelectedAdapter = _netInfoService.GetPrimaryActiveAdapter() ?? Adapters.FirstOrDefault();

        // Inicializar destinos de ping
        PingTargets.Clear();
        var defaultTargets = PingService.GetDefaultTargets(SelectedAdapter?.GatewayAddress);
        foreach (var target in defaultTargets)
        {
            PingTargets.Add(target);
        }
        SelectedPingTarget = PingTargets.FirstOrDefault();
    }

    public async Task LoadPublicIpAsync()
    {
        PublicIp = "Consultando...";
        var ip = await _publicIpService.GetPublicIpAsync();
        PublicIp = ip ?? "No disponible (Offline u Omitido)";
    }

    private void AddCustomTarget()
    {
        if (string.IsNullOrWhiteSpace(CustomTargetHost)) return;

        var host = CustomTargetHost.Trim();
        var newTarget = new PingTarget($"Personalizado ({host})", host, "Destino personalizado ingresado por el usuario", IsCustom: true);

        PingTargets.Add(newTarget);
        SelectedPingTarget = newTarget;
        CustomTargetHost = string.Empty;
        AppLogger.Info("Diagnóstico", $"Destino de ping personalizado añadido: {host}");
    }

    public async Task RunPingTestAsync()
    {
        if (SelectedPingTarget == null) return;

        // Guardia de licencia en ejecución
        if (!_licenseService.EnsureLicensed("Medición de Ping & Jitter"))
        {
            return;
        }

        IsTestingPing = true;
        try
        {
            AppLogger.Info("Diagnóstico", $"Iniciando test de Ping & Jitter hacia {SelectedPingTarget.Name} ({SelectedPingTarget.HostOrIp})...");
            var result = await _pingService.RunPingTestAsync(SelectedPingTarget.Name, SelectedPingTarget.HostOrIp);
            CurrentPingResult = result;

            PingHistory.Insert(0, result);
            if (PingHistory.Count > 20) PingHistory.RemoveAt(PingHistory.Count - 1);

            if (result.Success)
            {
                AppLogger.Success("Diagnóstico", $"Ping a {result.TargetName}: Prom {result.AvgLatencyMs}ms | Jitter {result.JitterMs:F1}ms | Pérdida {result.PacketLossPercent:F0}%");
            }
            else
            {
                AppLogger.Warn("Diagnóstico", $"Ping a {result.TargetName}: {result.StatusMessage}");
            }
        }
        finally
        {
            IsTestingPing = false;
        }
    }
}
