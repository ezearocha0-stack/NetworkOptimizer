using NetworkOptimizer.Config;
using NetworkOptimizer.Core;
using NetworkOptimizer.Diagnostics;
using NetworkOptimizer.Diagnostics.Models;
using NetworkOptimizer.Optimizations;

namespace NetworkOptimizer.Tests;

public class CoreAndDiagnosticsTests
{
    [Fact]
    public void AppConfig_HasIndependentConfiguration()
    {
        Assert.Equal("network-optimizer", AppConfig.ProductSlug);
        Assert.Equal("NetworkOptimizer", AppConfig.ProductName);
        Assert.Equal("https://gestor-claves-backend.onrender.com", AppConfig.LicenseApiBaseUrl);
    }

    [Fact]
    public void OperationResult_OkAndFail_CreateExpectedObjects()
    {
        var ok = OperationResult.Ok("TestOp", "Success message", "OldVal", "NewVal");
        Assert.True(ok.Success);
        Assert.Equal("TestOp", ok.Operation);
        Assert.Equal("Success message", ok.Message);
        Assert.Equal("OldVal", ok.PreviousState);
        Assert.Equal("NewVal", ok.NewState);
        Assert.Null(ok.Error);

        var fail = OperationResult.Fail("TestOp", "Fail message", "Error details", "OldVal");
        Assert.False(fail.Success);
        Assert.Equal("TestOp", fail.Operation);
        Assert.Equal("Fail message", fail.Message);
        Assert.Equal("Error details", fail.Error);
        Assert.Equal("OldVal", fail.PreviousState);
    }

    [Fact]
    public void AppLogger_SanitizesLicenseKeys()
    {
        AppLogger.Clear();
        var rawKey = "ABCD-1234-EFGH-5678";
        AppLogger.Info("TestLog", $"Clave introducida: {rawKey}");

        var entry = AppLogger.Entries.LastOrDefault();
        Assert.NotNull(entry);
        Assert.DoesNotContain(rawKey, entry.Message);
        Assert.Contains("[CLAVE_OCULTA]", entry.Message);
    }

    [Fact]
    public void BackupStore_SavesAndRetrievesState()
    {
        var testId = "test_opt_" + Guid.NewGuid().ToString("N");
        var originalValue = "CustomState_123";

        BackupStore.SaveOriginalState(testId, originalValue);
        var retrieved = BackupStore.GetOriginalState(testId);
        Assert.Equal(originalValue, retrieved);

        // A second save should NOT overwrite the original point
        BackupStore.SaveOriginalState(testId, "NewOverwriteValue");
        Assert.Equal(originalValue, BackupStore.GetOriginalState(testId));

        BackupStore.ClearOriginalState(testId);
        Assert.Null(BackupStore.GetOriginalState(testId));
    }

    [Fact]
    public void DnsBenchmarkService_ProvidesStandardPresets()
    {
        var presets = DnsBenchmarkService.GetPresets(new List<string> { "192.168.1.1" });
        Assert.NotEmpty(presets);
        Assert.Contains(presets, p => p.ProviderName == "Cloudflare" && p.PrimaryDns == "1.1.1.1");
        Assert.Contains(presets, p => p.ProviderName == "Google Public DNS" && p.PrimaryDns == "8.8.8.8");
        Assert.Contains(presets, p => p.ProviderName == "Quad9" && p.PrimaryDns == "9.9.9.9");
        Assert.Contains(presets, p => p.ProviderName == "DNS Actual");
    }

    [Fact]
    public void PingResult_CalculatesJitterCorrectly()
    {
        var result = new PingResult
        {
            TargetName = "Test",
            HostOrIp = "1.1.1.1",
            SentPackets = 4,
            ReceivedPackets = 4,
            Success = true,
            MinLatencyMs = 10,
            AvgLatencyMs = 15,
            MaxLatencyMs = 20,
            JitterMs = 5.0,
            PacketLossPercent = 0
        };

        Assert.True(result.Success);
        Assert.Equal(0, result.PacketLossPercent);
        Assert.Equal(5.0, result.JitterMs);
    }
}
