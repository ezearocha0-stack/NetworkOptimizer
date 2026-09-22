using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkOptimizer.Updates;
using NetworkOptimizer.Updates.Models;
using Xunit;

namespace NetworkOptimizer.Tests;

public class UpdateSystemTests
{
    // ==========================================
    // 1. COMPARACIÓN DE VERSIONES
    // ==========================================

    [Theory]
    [InlineData("1.0.0", "1.1.0", -1, true)]    // 1.0.0 < 1.1.0 -> remote is newer
    [InlineData("1.2.0", "1.10.0", -1, true)]   // 1.2.0 < 1.10.0 -> remote is newer
    [InlineData("1.0.0", "2.0.0", -1, true)]    // Remote major higher
    [InlineData("1.0.0", "1.0.1", -1, true)]    // Remote patch higher
    [InlineData("1.0.0", "v1.1.0", -1, true)]   // Prefix 'v'
    [InlineData("1.0.0", "1.0.0", 0, false)]    // Equal version
    [InlineData("1.0.0", "1.0.0.0", 0, false)]  // Equal normalized
    [InlineData("1.0.0", "v1.0.0", 0, false)]   // Equal with 'v'
    [InlineData("1.1.0", "1.0.0", 1, false)]    // Current is higher -> remote is lower
    [InlineData("1.10.0", "1.2.0", 1, false)]   // Current is higher -> remote is lower
    [InlineData("1.0.0", "0.9.0", 1, false)]    // Current is higher -> remote is lower
    public void VersionComparison_EvaluatesCorrectly(string current, string remote, int expectedComparisonSign, bool expectedIsNewer)
    {
        int result = UpdateVersionComparator.CompareVersions(current, remote);
        bool isNewer = UpdateVersionComparator.IsNewerVersion(current, remote);

        if (expectedComparisonSign > 0)
        {
            Assert.True(result > 0, $"Expected {current} > {remote}");
        }
        else if (expectedComparisonSign == 0)
        {
            Assert.Equal(0, result);
        }
        else
        {
            Assert.True(result < 0, $"Expected {current} < {remote}");
        }

        Assert.Equal(expectedIsNewer, isNewer);
    }

    [Fact]
    public void VersionComparison_InvalidVersionThrows()
    {
        Assert.Throws<ArgumentException>(() => UpdateVersionComparator.CompareVersions("not-a-version", "1.0.0"));
        Assert.Throws<ArgumentException>(() => UpdateVersionComparator.CompareVersions("1.0.0", "invalid"));
    }

    // ==========================================
    // 2. PARSEO Y VALIDACIÓN DE MANIFEST
    // ==========================================

    [Fact]
    public void ParseManifest_ValidJson_ReturnsPopulatedModel()
    {
        string validSha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        string json = $$"""
        {
            "version": "1.1.0",
            "downloadUrl": "https://tu-hosting.com/NetworkOptimizer/NetworkOptimizer-1.1.0.exe",
            "sha256": "{{validSha}}"
        }
        """;

        var manifest = UpdateService.ParseManifest(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.1.0", manifest.Version);
        Assert.Equal("https://tu-hosting.com/NetworkOptimizer/NetworkOptimizer-1.1.0.exe", manifest.DownloadUrl);
        Assert.Equal(validSha, manifest.Sha256);
    }

    [Theory]
    [InlineData("")]                                                    // JSON vacío
    [InlineData("{")]                                                   // JSON mal formado
    [InlineData("{\"downloadUrl\":\"https://test.com/app.exe\"}")]       // Falta version
    [InlineData("{\"version\":\"invalid\",\"downloadUrl\":\"https://test.com/app.exe\",\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}")] // Versión inválida
    [InlineData("{\"version\":\"1.1.0\",\"downloadUrl\":\"http://insecure.com/app.exe\",\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}")] // HTTP no seguro
    [InlineData("{\"version\":\"1.1.0\",\"downloadUrl\":\"not-a-url\",\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}")]       // URL inválida
    [InlineData("{\"version\":\"1.1.0\",\"downloadUrl\":\"https://test.com/app.exe\",\"sha256\":\"too-short\"}")] // Hash SHA-256 no tiene 64 chars
    [InlineData("{\"version\":\"1.1.0\",\"downloadUrl\":\"https://test.com/app.exe\",\"sha256\":\"zzzb0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\"}")] // Hash no hexadecimal
    public void ParseManifest_InvalidJsonOrFields_ReturnsNull(string invalidJson)
    {
        var manifest = UpdateService.ParseManifest(invalidJson);
        Assert.Null(manifest);
    }

    // ==========================================
    // 3. VERIFICACIÓN DE HASH SHA-256
    // ==========================================

    [Fact]
    public void HashVerifier_CorrectHash_ReturnsTrue()
    {
        byte[] content = Encoding.UTF8.GetBytes("Binary portable payload simulation NetworkOptimizer 1.1.0");
        byte[] hashBytes = SHA256.HashData(content);
        string expectedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, content);
            bool verified = UpdateHashVerifier.VerifySha256(tempFile, expectedHash);
            Assert.True(verified);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void HashVerifier_IncorrectHash_ReturnsFalse()
    {
        byte[] content = Encoding.UTF8.GetBytes("Original untampered content");
        string corruptedHash = "0000000000000000000000000000000000000000000000000000000000000000";

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, content);
            bool verified = UpdateHashVerifier.VerifySha256(tempFile, corruptedHash);
            Assert.False(verified);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void HashVerifier_CaseInsensitiveHash_ReturnsTrue()
    {
        byte[] content = Encoding.UTF8.GetBytes("Case test");
        byte[] hashBytes = SHA256.HashData(content);
        string uppercaseHash = Convert.ToHexString(hashBytes).ToUpperInvariant();

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, content);
            bool verified = UpdateHashVerifier.VerifySha256(tempFile, uppercaseHash);
            Assert.True(verified);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ==========================================
    // 4. FLUJO DE CHECKFORUPDATE CON HTTP SIMULADO
    // ==========================================

    [Fact]
    public async Task CheckForUpdate_WhenRemoteIsSuperior_DetectsUpdate()
    {
        string manifestJson = """
        {
            "version": "1.5.0",
            "downloadUrl": "https://test-server.com/NetworkOptimizer/NetworkOptimizer-1.5.0.exe",
            "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
        """;

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.True(result.IsAvailable);
        Assert.NotNull(result.Manifest);
        Assert.Equal("1.5.0", result.NewVersion);
    }

    [Fact]
    public async Task CheckForUpdate_WhenRemoteIsEqual_ReportsAlreadyUpdated()
    {
        string currentVer = Config.AppConfig.AppVersion;
        string manifestJson = $$"""
        {
            "version": "{{currentVer}}",
            "downloadUrl": "https://test-server.com/NetworkOptimizer/NetworkOptimizer-{{currentVer}}.exe",
            "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
        """;

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.False(result.IsAvailable);
        Assert.Contains("actualizada", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdate_WhenRemoteIsLower_ReportsNoUpdate()
    {
        string manifestJson = """
        {
            "version": "0.8.0",
            "downloadUrl": "https://test-server.com/NetworkOptimizer/NetworkOptimizer-0.8.0.exe",
            "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        }
        """;

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task CheckForUpdate_WhenNetworkFailsOrOffline_DoesNotThrowAndReturnsCleanResult()
    {
        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            throw new HttpRequestException("Simulated offline/no internet connection");
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.False(result.IsAvailable);
        Assert.Contains("sin conexión", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdate_WhenServerReturns404_ReturnsGracefulResult()
    {
        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.False(result.IsAvailable);
        Assert.Contains("404", result.Message);
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("http://insecure-server.com/latest.json")]
    [InlineData("ftp://ftp.example.com/latest.json")]
    public async Task CheckForUpdate_WhenManifestUrlIsInvalidOrInsecure_ReturnsGracefulResult(string invalidUrl)
    {
        using var client = new HttpClient();
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync(invalidUrl);

        Assert.False(result.IsAvailable);
        Assert.Contains("URL de manifiesto inválida", result.Message);
    }

    [Fact]
    public async Task CheckForUpdate_WhenManifestContentIsCorrupted_ReturnsGracefulResult()
    {
        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>502 Bad Gateway</body></html>", Encoding.UTF8, "text/html")
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync("https://test-server.com/latest.json");

        Assert.False(result.IsAvailable);
        Assert.Contains("no válido", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================
    // 5. DESCARGA Y VERIFICACIÓN DE SHA-256 CON CORRUPCIÓN
    // ==========================================

    [Fact]
    public async Task DownloadAndVerify_WhenHashMatches_SucceedsAndPreservesTempFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Valid executable binary content");
        string validSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var manifest = new UpdateManifest("1.2.0", "https://tu-hosting.com/download/app.exe", validSha);

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.DownloadAndVerifyUpdateAsync(manifest);

        try
        {
            Assert.True(result.Success);
            Assert.NotNull(result.DownloadedFilePath);
            Assert.True(File.Exists(result.DownloadedFilePath));
        }
        finally
        {
            if (!string.IsNullOrEmpty(result.DownloadedFilePath) && File.Exists(result.DownloadedFilePath))
            {
                File.Delete(result.DownloadedFilePath);
            }
        }
    }

    [Fact]
    public async Task DownloadAndVerify_WhenHashMismatches_FailsAndDeletesCorruptedFile()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Tampered or corrupted executable binary");
        string wrongSha = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        var manifest = new UpdateManifest("1.2.0", "https://tu-hosting.com/download/app.exe", wrongSha);

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.DownloadAndVerifyUpdateAsync(manifest);

        Assert.False(result.Success);
        Assert.Contains("SHA-256 no coincide", result.ErrorMessage);
        // Verificar que el archivo temporal corrupto fue eliminado
        if (!string.IsNullOrEmpty(result.DownloadedFilePath))
        {
            Assert.False(File.Exists(result.DownloadedFilePath), "Corrupted download file MUST be deleted!");
        }
    }

    // ==========================================
    // 6. INTEGRACIÓN GITHUB RELEASES Y LICENCIA
    // ==========================================

    [Fact]
    public void AppConfig_UpdateManifestUrl_ConfiguredWithOfficialGitHubRepository()
    {
        string url = Config.AppConfig.UpdateManifestUrl;

        Assert.Equal("https://raw.githubusercontent.com/ezearocha0-stack/NetworkOptimizer/main/latest.json", url);

        var uri = new Uri(url);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("raw.githubusercontent.com", uri.Host);
        Assert.Equal("/ezearocha0-stack/NetworkOptimizer/main/latest.json", uri.AbsolutePath);
    }

    [Fact]
    public void GitHubReleaseManifest_ValidReleaseStructure_ParsesAndValidatesSuccessfully()
    {
        string validSha = "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e";
        string json = $$"""
        {
            "version": "1.1.0",
            "downloadUrl": "https://github.com/ezearocha0-stack/NetworkOptimizer/releases/download/v1.1.0/NetworkOptimizer.exe",
            "sha256": "{{validSha}}"
        }
        """;

        var manifest = UpdateService.ParseManifest(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.1.0", manifest.Version);
        Assert.Equal("https://github.com/ezearocha0-stack/NetworkOptimizer/releases/download/v1.1.0/NetworkOptimizer.exe", manifest.DownloadUrl);
        Assert.Equal(validSha, manifest.Sha256);

        // Verificar que 1.1.0 es detectado como versión superior a la versión base 1.0.0
        Assert.True(UpdateVersionComparator.IsNewerVersion("1.0.0", manifest.Version));
    }

    [Fact]
    public async Task CheckForUpdate_WhenGitHubReturns404ForLatestJson_AppContinuesSafely()
    {
        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            Assert.Equal("https://raw.githubusercontent.com/ezearocha0-stack/NetworkOptimizer/main/latest.json", req.RequestUri?.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync();

        Assert.False(result.IsAvailable);
        Assert.Null(result.Manifest);
        Assert.Contains("404", result.Message);
    }

    [Fact]
    public async Task CheckForUpdate_WhenGitHubIsUnavailableOrOffline_AppContinuesSafely()
    {
        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            throw new HttpRequestException("GitHub DNS resolution failure or network down");
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        var result = await updateService.CheckForUpdateAsync();

        Assert.False(result.IsAvailable);
        Assert.Null(result.Manifest);
        Assert.Contains("sin conexión", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LicensePreservation_UpdateCheckDoesNotAlterLicenseState()
    {
        // Verificar que el estado de la licencia se mantenga intacto antes y después de una comprobación de actualización
        var licenseService = new Licensing.LicenseService();
        bool initialLicensed = licenseService.IsLicensed;
        string initialStatus = licenseService.GetCachedStatus().StatusText;

        var fakeHandler = new MockHttpMessageHandler((req) =>
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(fakeHandler);
        var updateService = new UpdateService(client);

        await updateService.CheckForUpdateAsync();

        // El estado y las propiedades de licencia deben ser exactamente iguales
        Assert.Equal(initialLicensed, licenseService.IsLicensed);
        Assert.Equal(initialStatus, licenseService.GetCachedStatus().StatusText);
        Assert.Equal("network-optimizer", Config.AppConfig.ProductSlug);
    }

    // Helper mock HTTP handler
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
