namespace NetworkOptimizer.Tests;

public class UnitTest1
{
    [Fact]
    public void TestLicensingTypes()
    {
        var config = new global::Licensing.Client.Configuration.ClientConfig
        {
            ProductSlug = "network-optimizer",
            ProductName = "NetworkOptimizer",
            ClientVersion = "1.0.0",
            ApiBaseUrl = "https://gestor-claves-backend.onrender.com"
        };
        var client = new global::Licensing.Client.Licensing.LicenseClient(config);
        Assert.NotNull(client);
        Assert.Equal("network-optimizer", config.ProductSlug);
    }
}
