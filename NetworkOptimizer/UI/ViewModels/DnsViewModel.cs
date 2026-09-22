using System.Collections.ObjectModel;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics;
using NetworkOptimizer.Diagnostics.Models;
using NetworkOptimizer.Licensing;
using NetworkOptimizer.Optimizations;

namespace NetworkOptimizer.UI.ViewModels;

public class DnsViewModel : ObservableObject
{
    private readonly DnsBenchmarkService _dnsBenchmarkService = new();
    private readonly DnsOptimization _dnsOptimization = new();
    private readonly NetworkInfoService _netInfoService = new();
    private readonly LicenseService _licenseService;

    private string _activeAdapterName = "Ethernet";
    private string _currentDnsDisplay = "Cargando...";
    private bool _isBenchmarking;
    private bool _isApplying;
    private string _statusMessage = "Listo para iniciar benchmark o aplicar preset.";
    private DnsServerPreset? _selectedPreset;
    private bool _canRollback;

    public ObservableCollection<DnsBenchmarkResult> BenchmarkResults { get; } = new();
    public ObservableCollection<DnsServerPreset> Presets { get; } = new();

    public bool IsLicensed => _licenseService.IsLicensed;

    public string ActiveAdapterName
    {
        get => _activeAdapterName;
        set => SetField(ref _activeAdapterName, value);
    }

    public string CurrentDnsDisplay
    {
        get => _currentDnsDisplay;
        set => SetField(ref _currentDnsDisplay, value);
    }

    public bool IsBenchmarking
    {
        get => _isBenchmarking;
        set => SetField(ref _isBenchmarking, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        set => SetField(ref _isApplying, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public DnsServerPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetField(ref _selectedPreset, value);
    }

    public bool CanRollback
    {
        get => _canRollback;
        set => SetField(ref _canRollback, value);
    }

    public AsyncRelayCommand RunBenchmarkCommand { get; }
    public AsyncRelayCommand ApplySelectedPresetCommand { get; }
    public AsyncRelayCommand RestoreDhcpCommand { get; }
    public AsyncRelayCommand RollbackDnsCommand { get; }
    public AsyncRelayCommand RefreshCurrentDnsCommand { get; }

    public DnsViewModel() : this(new LicenseService())
    {
    }

    public DnsViewModel(LicenseService licenseService)
    {
        _licenseService = licenseService;

        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync, () => !IsBenchmarking && !IsApplying && _licenseService.IsLicensed);
        ApplySelectedPresetCommand = new AsyncRelayCommand(ApplySelectedPresetAsync, () => !IsApplying && SelectedPreset != null && _licenseService.IsLicensed);
        RestoreDhcpCommand = new AsyncRelayCommand(RestoreDhcpAsync, () => !IsApplying && _licenseService.IsLicensed);
        RollbackDnsCommand = new AsyncRelayCommand(RollbackDnsAsync, () => !IsApplying && CanRollback && _licenseService.IsLicensed);
        RefreshCurrentDnsCommand = new AsyncRelayCommand(RefreshDnsInfoAsync);

        _licenseService.LicenseStateChanged += _ =>
        {
            OnPropertyChanged(nameof(IsLicensed));
            RunBenchmarkCommand.RaiseCanExecuteChanged();
            ApplySelectedPresetCommand.RaiseCanExecuteChanged();
            RestoreDhcpCommand.RaiseCanExecuteChanged();
            RollbackDnsCommand.RaiseCanExecuteChanged();
        };

        _ = RefreshDnsInfoAsync();
    }

    public async Task RefreshDnsInfoAsync()
    {
        var primary = _netInfoService.GetPrimaryActiveAdapter();
        if (primary != null)
        {
            ActiveAdapterName = primary.Name;
            CurrentDnsDisplay = primary.DnsServersDisplay;
        }
        else
        {
            CurrentDnsDisplay = "No se detectó adaptador activo";
        }

        CanRollback = _dnsOptimization.HasRollback(ActiveAdapterName);

        Presets.Clear();
        var presets = DnsBenchmarkService.GetPresets(primary?.DnsServers);
        foreach (var p in presets)
        {
            Presets.Add(p);
        }

        if (SelectedPreset == null)
        {
            SelectedPreset = Presets.FirstOrDefault(p => p.ProviderName == "Cloudflare") ?? Presets.FirstOrDefault();
        }
    }

    public async Task RunBenchmarkAsync()
    {
        if (!_licenseService.EnsureLicensed("DNS Speed Benchmark"))
        {
            StatusMessage = "Se requiere una licencia activa para ejecutar el benchmark de DNS.";
            return;
        }

        IsBenchmarking = true;
        StatusMessage = "Ejecutando consultas de resolución DNS en tiempo real...";
        BenchmarkResults.Clear();

        try
        {
            AppLogger.Info("DNS Benchmark", "Iniciando prueba comparativa de servidores DNS...");
            var results = await _dnsBenchmarkService.BenchmarkAllPresetsAsync(Presets.ToList());

            foreach (var res in results)
            {
                BenchmarkResults.Add(res);
            }

            var fastest = results.FirstOrDefault(r => r.IsAvailable);
            if (fastest != null)
            {
                StatusMessage = $"Benchmark completado. Menor tiempo de respuesta: {fastest.ProviderName} ({fastest.ResponseTimeDisplay}).";
                AppLogger.Success("DNS Benchmark", StatusMessage);
            }
            else
            {
                StatusMessage = "Benchmark completado. Ningún servidor respondió en tiempo límite.";
            }
        }
        finally
        {
            IsBenchmarking = false;
        }
    }

    public async Task ApplySelectedPresetAsync()
    {
        if (SelectedPreset == null) return;

        if (!_licenseService.EnsureLicensed("Configuración de Servidores DNS"))
        {
            StatusMessage = "Se requiere una licencia activa para configurar servidores DNS.";
            return;
        }

        IsApplying = true;
        StatusMessage = $"Configurando {SelectedPreset.ProviderName}...";

        try
        {
            var result = await _dnsOptimization.ApplyDnsPresetAsync(ActiveAdapterName, SelectedPreset);
            StatusMessage = result.Message;
            await RefreshDnsInfoAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }

    public async Task RestoreDhcpAsync()
    {
        if (!_licenseService.EnsureLicensed("Restauración de DNS a DHCP"))
        {
            StatusMessage = "Se requiere una licencia activa para modificar la configuración de red.";
            return;
        }

        IsApplying = true;
        StatusMessage = "Restaurando DNS a automático (DHCP)...";

        try
        {
            var result = await _dnsOptimization.RestoreDhcpDnsAsync(ActiveAdapterName);
            StatusMessage = result.Message;
            await RefreshDnsInfoAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }

    public async Task RollbackDnsAsync()
    {
        if (!_licenseService.EnsureLicensed("Rollback de Configuración DNS"))
        {
            StatusMessage = "Se requiere una licencia activa para restaurar la configuración de red.";
            return;
        }

        IsApplying = true;
        StatusMessage = "Restaurando configuración original de DNS...";

        try
        {
            var result = await _dnsOptimization.RollbackDnsAsync(ActiveAdapterName);
            StatusMessage = result.Message;
            await RefreshDnsInfoAsync();
        }
        finally
        {
            IsApplying = false;
        }
    }
}
