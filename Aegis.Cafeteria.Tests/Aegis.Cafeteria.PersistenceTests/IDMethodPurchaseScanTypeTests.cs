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

    [Fact]
    public void ServicesConfiguration_PurchaseScanType_Is8()
    {
        using JsonDocument document = LoadJson("Aegis.Cafeteria.Services/appsettings.json");
        JsonElement ffp = document.RootElement.GetProperty("FfpIntegration");
        int purchaseScanType = ffp.GetProperty("PurchaseScanType").GetInt32();

        Assert.Equal(8, purchaseScanType);
        Assert.NotEqual(9, purchaseScanType);
    }

    [Theory]
    [InlineData("Aegis.Cafeteria.UI/appsettings.json")]
    [InlineData("Aegis.Cafeteria.UI.Linux/appsettings.json")]
    public void PosConfiguration_DoesNotContainFfpIntegration(string relativePath)
    {
        using JsonDocument document = LoadJson(relativePath);
        Assert.False(document.RootElement.TryGetProperty("FfpIntegration", out _));
    }

    private static JsonDocument LoadJson(string relativePath)
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria.Services")) &&
                Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria.Tests")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Aegis.Cafeteria repository root from the test output directory.");
    }
}
