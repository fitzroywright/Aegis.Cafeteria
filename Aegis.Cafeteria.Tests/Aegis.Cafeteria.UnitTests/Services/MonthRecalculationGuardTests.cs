namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;

public sealed class MonthRecalculationGuardTests
{
    [Theory]
    [InlineData(1999)]
    [InlineData(10000)]
    public async Task CheckAsync_RejectsInvalidYear(int year)
    {
        FfpMonthRecalculationGuard guard = CreateGuard();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>
        (
            () => guard.CheckAsync(year, 8)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task CheckAsync_RejectsInvalidMonth(int month)
    {
        FfpMonthRecalculationGuard guard = CreateGuard();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>
        (
            () => guard.CheckAsync(2026, month)
        );
    }

    private static FfpMonthRecalculationGuard CreateGuard()
    {
        FfpIntegrationOptions options = new()
        {
              Enabled = true
            , ConnectionString = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true"
        };

        FfpSqlConnectionFactory connections = new(options);
        return new FfpMonthRecalculationGuard(connections);
    }
}
