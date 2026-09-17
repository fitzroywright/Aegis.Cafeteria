namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class MonthRecalculationGuardTests
{
    [Theory]
    [InlineData(1999)]
    [InlineData(10000)]
    public async Task CheckAsync_RejectsInvalidYear(int year)
    {
        FfpSqlApplicationGateway gateway = CreateGateway();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>
        (
            () => gateway.CheckAsync(year, 8)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task CheckAsync_RejectsInvalidMonth(int month)
    {
        FfpSqlApplicationGateway gateway = CreateGateway();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>
        (
            () => gateway.CheckAsync(2026, month)
        );
    }

    private static FfpSqlApplicationGateway CreateGateway()
    {
        FfpIntegrationOptions options = new()
        {
            Enabled = true,
            ConnectionString = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true"
        };

        return new FfpSqlApplicationGateway
        (
            options,
            NullLogger<FfpSqlApplicationGateway>.Instance
        );
    }
}
