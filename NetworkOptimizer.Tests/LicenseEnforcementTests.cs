using System;
using System.Threading;
using System.Threading.Tasks;
using Licensing.Client.Configuration;
using Licensing.Client.Licensing;
using Licensing.Client.Models;
using Licensing.Client.Storage;
using NetworkOptimizer.Licensing;
using NetworkOptimizer.UI.ViewModels;
using Xunit;

namespace NetworkOptimizer.Tests;

public class FakeLicenseClient : ILicenseClient
{
    public LicenseCacheData? CachedData { get; set; }
    public Func<string, Task<LicenseResult>>? ValidateHandler { get; set; }
    public Func<string, string, Task<LicenseResult>>? ActivateHandler { get; set; }
    public bool ClearedLocalLicense { get; set; }

    public Task<LicenseResult> ValidateAsync(string? key = null, CancellationToken ct = default)
    {
        if (ValidateHandler != null && key != null) return ValidateHandler(key);
        return Task.FromResult(new LicenseResult
        {
            IsValid = false,
            ValidationOutcome = ValidationOutcome.InvalidKey,
            Message = "Invalid Key"
        });
    }

    public Task<LicenseResult> ActivateAsync(string key, string? machineName = null, CancellationToken ct = default)
    {
        if (ActivateHandler != null && machineName != null) return ActivateHandler(key, machineName);
        return Task.FromResult(new LicenseResult
        {
            IsValid = false,
            ActivationOutcome = ActivationOutcome.InvalidKey,
            Message = "Invalid Key"
        });
    }

    public string GetDeviceFingerprint() => "TEST-HWID-12345";

    public LicenseCacheData? GetCachedLicense() => CachedData;

    public void ClearLocalLicense()
    {
        CachedData = null;
        ClearedLocalLicense = true;
    }
}

public class LicenseEnforcementTests
{
    private static ClientConfig CreateConfig() => new()
    {
        ProductSlug = "network-optimizer",
        ProductName = "NetworkOptimizer",
        ClientVersion = "1.0.0",
        ApiBaseUrl = "https://gestor-claves-backend.onrender.com"
    };

    [Fact]
    public void Unlicensed_BlocksAccessAndSetsIsLicensedFalse()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());

        Assert.False(service.IsLicensed);
        Assert.False(service.EnsureLicensed("Ping"));

        var status = service.GetCachedStatus();
        Assert.False(status.IsLicensed);
        Assert.Equal("No activado / Sin licencia", status.StatusText);
    }

    [Fact]
    public void ValidCachedLicense_EnablesAccess()
    {
        var fakeClient = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "VALID-KEY",
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Active",
                LastValidatedUtc = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMonths(1)
            }
        };

        var service = new LicenseService(fakeClient, CreateConfig());

        Assert.True(service.IsLicensed);
        Assert.True(service.EnsureLicensed("Ping"));

        var status = service.GetCachedStatus();
        Assert.True(status.IsLicensed);
    }

    [Fact]
    public void ExpiredLicense_BlocksAccess()
    {
        var fakeClient = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "EXPIRED-KEY",
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Active",
                LastValidatedUtc = DateTimeOffset.UtcNow.AddDays(-10),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
            }
        };

        var service = new LicenseService(fakeClient, CreateConfig());

        Assert.False(service.IsLicensed);
        Assert.False(service.EnsureLicensed("Ping"));

        var status = service.GetCachedStatus();
        Assert.False(status.IsLicensed);
        Assert.Equal("Licencia expirada", status.StatusText);
    }

    [Fact]
    public void RevokedOrSuspendedLicense_BlocksAccess()
    {
        var fakeClientRevoked = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "REVOKED-KEY",
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Revoked",
                LastValidatedUtc = DateTimeOffset.UtcNow
            }
        };

        var serviceRevoked = new LicenseService(fakeClientRevoked, CreateConfig());
        Assert.False(serviceRevoked.IsLicensed);
        Assert.False(serviceRevoked.EnsureLicensed("TCP Optimization"));
        Assert.Equal("Licencia revocada", serviceRevoked.GetCachedStatus().StatusText);

        var fakeClientSuspended = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "SUSPENDED-KEY",
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Suspended",
                LastValidatedUtc = DateTimeOffset.UtcNow
            }
        };

        var serviceSuspended = new LicenseService(fakeClientSuspended, CreateConfig());
        Assert.False(serviceSuspended.IsLicensed);
        Assert.False(serviceSuspended.EnsureLicensed("TCP Optimization"));
        Assert.Equal("Licencia suspendida", serviceSuspended.GetCachedStatus().StatusText);
    }

    [Fact]
    public void ExpiredOfflineGracePeriod_BlocksAccess()
    {
        var fakeClient = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "GRACE-KEY",
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Active",
                // Validado hace 5 días (el grace period por defecto es 2 días + 10 min tolerancia)
                LastValidatedUtc = DateTimeOffset.UtcNow.AddDays(-5),
                ExpiresAt = DateTimeOffset.UtcNow.AddMonths(1)
            }
        };

        var service = new LicenseService(fakeClient, CreateConfig());

        Assert.False(service.IsLicensed);
        Assert.False(service.EnsureLicensed("DNS Benchmark"));
        Assert.Equal("Período de gracia offline expirado", service.GetCachedStatus().StatusText);
    }

    [Fact]
    public async Task Activation_EnablesAccess_AndDeactivation_BlocksAccess()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());

        Assert.False(service.IsLicensed);

        fakeClient.ActivateHandler = (key, machine) =>
        {
            fakeClient.CachedData = new LicenseCacheData
            {
                LicenseKey = key,
                Hwid = "TEST-HWID-12345",
                ProductName = "network-optimizer",
                LastKnownStatus = "Active",
                LastValidatedUtc = DateTimeOffset.UtcNow
            };

            return Task.FromResult(new LicenseResult
            {
                IsValid = true,
                ActivationOutcome = ActivationOutcome.Success,
                Product = "network-optimizer",
                LicenseKey = key,
                Message = "Activado"
            });
        };

        bool eventFired = false;
        service.LicenseStateChanged += state => eventFired = state;

        var (success, _, status) = await service.ActivateKeyAsync("VALID-KEY");
        Assert.True(success);
        Assert.True(service.IsLicensed);
        Assert.True(eventFired);
        Assert.True(status.IsLicensed);

        // Desactivación
        bool deactivationEventFired = false;
        service.LicenseStateChanged += state => { if (!state) deactivationEventFired = true; };

        service.Deactivate();
        Assert.False(service.IsLicensed);
        Assert.True(fakeClient.ClearedLocalLicense);
        Assert.True(deactivationEventFired);
        Assert.False(service.EnsureLicensed("Ping"));
    }

    [Fact]
    public void DashboardViewModel_RequiresLicenseForPingCommand()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());
        var vm = new DashboardViewModel(service);

        // Sin licencia: CanExecute debe ser falso
        Assert.False(vm.IsLicensed);
        Assert.False(vm.RunPingCommand.CanExecute(null));

        // Llamar directamente al método no debe ejecutar el ping
        _ = vm.RunPingTestAsync();
        Assert.Null(vm.CurrentPingResult);
    }

    [Fact]
    public void DnsViewModel_RequiresLicenseForDnsOperations()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());
        var vm = new DnsViewModel(service);

        Assert.False(vm.IsLicensed);
        Assert.False(vm.RunBenchmarkCommand.CanExecute(null));
        Assert.False(vm.ApplySelectedPresetCommand.CanExecute(null));
        Assert.False(vm.RestoreDhcpCommand.CanExecute(null));
        Assert.False(vm.RollbackDnsCommand.CanExecute(null));

        // Handlers directos abortan con mensaje de advertencia
        _ = vm.RunBenchmarkAsync();
        Assert.Empty(vm.BenchmarkResults);
        Assert.Contains("Se requiere una licencia activa", vm.StatusMessage);
    }

    [Fact]
    public void OptimizationsViewModel_RequiresLicenseForApplyingSettings()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());
        var vm = new OptimizationsViewModel(service);

        Assert.False(vm.IsLicensed);
        Assert.False(vm.ApplyTcpCommand.CanExecute(null));
        Assert.False(vm.RollbackTcpCommand.CanExecute(null));
        Assert.False(vm.ApplyRssCommand.CanExecute(null));
        Assert.False(vm.RollbackRssCommand.CanExecute(null));
    }

    [Fact]
    public void MaintenanceViewModel_RequiresLicenseForMaintenanceTasks()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());
        var vm = new MaintenanceViewModel(service);

        Assert.False(vm.IsLicensed);
        Assert.False(vm.FlushDnsCommand.CanExecute(null));
        Assert.False(vm.FlushArpCommand.CanExecute(null));
        Assert.False(vm.RenewDhcpCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidLicenseValidation_KeepsAccessBlocked()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        fakeClient.ValidateHandler = _ => Task.FromResult(new LicenseResult
        {
            IsValid = false,
            ValidationOutcome = ValidationOutcome.InvalidKey,
            Message = "Clave inexistente"
        });

        var service = new LicenseService(fakeClient, CreateConfig());
        var status = await service.ValidateCurrentLicenseAsync("MALICIOUS-KEY");

        Assert.False(status.IsLicensed);
        Assert.False(service.IsLicensed);
        Assert.Contains("No válida", status.StatusText);
        Assert.False(service.EnsureLicensed("TCP Auto-Tuning"));
    }

    [Fact]
    public void RestartAppWithoutLicense_ContinuesBlocked()
    {
        // Sesión 1: Usuario desvincula licencia
        var fakeClient = new FakeLicenseClient
        {
            CachedData = new LicenseCacheData
            {
                LicenseKey = "OLD-KEY",
                LastKnownStatus = "Active",
                LastValidatedUtc = DateTimeOffset.UtcNow
            }
        };

        var service1 = new LicenseService(fakeClient, CreateConfig());
        Assert.True(service1.IsLicensed);

        service1.Deactivate();
        Assert.False(service1.IsLicensed);
        Assert.Null(fakeClient.GetCachedLicense());

        // Sesión 2: Simulación de cierre y apertura de la aplicación (nueva instancia con misma caché)
        var service2 = new LicenseService(fakeClient, CreateConfig());
        Assert.False(service2.IsLicensed);
        Assert.False(service2.EnsureLicensed("Flush DNS"));
        Assert.Equal("No activado / Sin licencia", service2.GetCachedStatus().StatusText);
    }

    [Fact]
    public async Task OptimizationsAndMaintenance_HandlersDirectlyBlockedWithoutLicense()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());

        var optVm = new OptimizationsViewModel(service);
        var maintVm = new MaintenanceViewModel(service);

        // 1. Barrera a nivel de Command
        Assert.False(optVm.ApplyTcpCommand.CanExecute(null));
        Assert.False(optVm.RollbackTcpCommand.CanExecute(null));
        Assert.False(optVm.ApplyRssCommand.CanExecute(null));
        Assert.False(optVm.RollbackRssCommand.CanExecute(null));
        Assert.False(maintVm.FlushDnsCommand.CanExecute(null));
        Assert.False(maintVm.FlushArpCommand.CanExecute(null));
        Assert.False(maintVm.RenewDhcpCommand.CanExecute(null));

        // 2. Barrera a nivel de Handler directo (EnsureLicensed)
        await optVm.ApplyTcpAsync();
        Assert.Contains("Se requiere una licencia activa", optVm.StatusNotice);

        await optVm.RollbackTcpAsync();
        Assert.Contains("Se requiere una licencia activa", optVm.StatusNotice);

        await optVm.ApplyRssAsync();
        Assert.Contains("Se requiere una licencia activa", optVm.StatusNotice);

        await optVm.RollbackRssAsync();
        Assert.Contains("Se requiere una licencia activa", optVm.StatusNotice);

        await maintVm.FlushDnsAsync();
        Assert.Contains("Se requiere una licencia activa", maintVm.LastActionStatus);

        await maintVm.FlushArpAsync();
        Assert.Contains("Se requiere una licencia activa", maintVm.LastActionStatus);

        await maintVm.RenewDhcpAsync();
        Assert.Contains("Se requiere una licencia activa", maintVm.LastActionStatus);
    }

    [Fact]
    public async Task MainViewModel_SynchronizesLicenseStateAcrossAllViewModels()
    {
        var fakeClient = new FakeLicenseClient { CachedData = null };
        var service = new LicenseService(fakeClient, CreateConfig());

        var mainVm = new MainViewModel(service);

        // Inicialmente sin licencia
        Assert.False(mainVm.IsLicensed);
        Assert.False(mainVm.Dashboard.IsLicensed);
        Assert.False(mainVm.Dns.IsLicensed);
        Assert.False(mainVm.Optimizations.IsLicensed);
        Assert.False(mainVm.Maintenance.IsLicensed);
        Assert.False(mainVm.Dashboard.RunPingCommand.CanExecute(null));
        Assert.False(mainVm.Maintenance.FlushDnsCommand.CanExecute(null));

        fakeClient.ActivateHandler = (key, machine) =>
        {
            fakeClient.CachedData = new LicenseCacheData
            {
                LicenseKey = key,
                Hwid = "TEST-HWID",
                ProductName = "network-optimizer",
                LastKnownStatus = "Active",
                LastValidatedUtc = DateTimeOffset.UtcNow
            };

            return Task.FromResult(new LicenseResult
            {
                IsValid = true,
                ActivationOutcome = ActivationOutcome.Success,
                Product = "network-optimizer",
                LicenseKey = key,
                Message = "Activado"
            });
        };

        // Activamos desde LicensingViewModel
        mainVm.Licensing.LicenseKeyInput = "MY-NEW-KEY";
        mainVm.Licensing.ActivateKeyCommand.Execute(null);

        // Esperamos brevemente la resolución de la tarea
        await Task.Delay(100);

        // Todas las pestañas deben sincronizarse a activado
        Assert.True(mainVm.IsLicensed);
        Assert.True(mainVm.Dashboard.IsLicensed);
        Assert.True(mainVm.Dns.IsLicensed);
        Assert.True(mainVm.Optimizations.IsLicensed);
        Assert.True(mainVm.Maintenance.IsLicensed);

        // Desactivación inmediata
        mainVm.Licensing.DeactivateCommand.Execute(null);

        Assert.False(mainVm.IsLicensed);
        Assert.False(mainVm.Dashboard.IsLicensed);
        Assert.False(mainVm.Dns.IsLicensed);
        Assert.False(mainVm.Optimizations.IsLicensed);
        Assert.False(mainVm.Maintenance.IsLicensed);
        Assert.False(mainVm.Dashboard.RunPingCommand.CanExecute(null));
        Assert.False(mainVm.Maintenance.FlushDnsCommand.CanExecute(null));
    }
}
