namespace Aegis.Cafeteria.PersistenceTests;

using System.Text.Json;
using Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class IDMethodPurchaseScanTypeTests
{
    [Fact]
    public void FfpIntegrationOptions_DefaultPurchaseScanType_Is8()
    {
        FfpIntegrationOptions options = new();
        Assert.Equal(8, options.PurchaseScanType);
        Assert.NotEqual(9, options.PurchaseScanType);
    }

    [Theory]
    [InlineData("Aegis.Cafeteria/Aegis.Cafeteria.Services/appsettings.json")]
    [InlineData("Aegis.Cafeteria/Aegis.Cafeteria.UI/appsettings.json")]
    public void ShippedConfiguration_PurchaseScanType_Is8(string relativePath)
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement ffp = document.RootElement.GetProperty("FfpIntegration");
        int purchaseScanType = ffp.GetProperty("PurchaseScanType").GetInt32();

        Assert.Equal(8, purchaseScanType);
        Assert.NotEqual(9, purchaseScanType);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria")) &&
                Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria.Tests")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Aegis.Cafeteria repository root from the test output directory.");
    }
}
