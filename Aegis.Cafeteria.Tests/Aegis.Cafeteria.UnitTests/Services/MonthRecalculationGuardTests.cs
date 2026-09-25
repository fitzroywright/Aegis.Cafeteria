namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Services.Reporting;

public sealed class MonthRecalculationGuardTests
{
    [Theory]
    [InlineData(1999)]
    [InlineData(10000)]
    public async Task CheckAsync_RejectsInvalidYear(int year)
    {
        CafeteriaMonthRecalculationGuard guard = CreateGuard();

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
        CafeteriaMonthRecalculationGuard guard = CreateGuard();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>
        (
            () => guard.CheckAsync(2026, month)
        );
    }

    private static CafeteriaMonthRecalculationGuard CreateGuard() => new(null!);
}
