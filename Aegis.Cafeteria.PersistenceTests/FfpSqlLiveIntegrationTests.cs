namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Opt-in test against the real FFP SQL Server. It does not run unless AEGIS_FFP_LIVE_TEST=1.
/// This keeps normal test runs deterministic while giving deployment machines a direct schema/connectivity check.
/// </summary>
public sealed class FfpSqlLiveIntegrationTests
{
    [Fact]
    public async Task LiveFfp_EmployeesAndCurrentYearHolidays_CanBeRead()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("AEGIS_FFP_LIVE_TEST"), "1", StringComparison.OrdinalIgnoreCase))
            return;

        var connectionString = Environment.GetEnvironmentVariable("AEGIS_FFP_TEST_CONNECTION_STRING");
        var options = new FfpIntegrationOptions
        {
            Enabled = true,
            ConnectionString = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : throw new InvalidOperationException("Set AEGIS_FFP_TEST_CONNECTION_STRING when AEGIS_FFP_LIVE_TEST=1.")
        };
        var gateway = new FfpSqlApplicationGateway(options, NullLogger<FfpSqlApplicationGateway>.Instance);

        var employees = await gateway.GetCustomersAsync();
        Assert.NotEmpty(employees);
        Assert.All
        (
              employees.Take(20)
            , employee =>
            {
                Assert.True(employee.CustomerId > 0);
                Assert.True(employee.IsEligibleForCafeteriaCredit);
            }
        );

        var today = DateOnly.FromDateTime(DateTime.Today);
        var holidays = await gateway.GetHolidaysAsync(today.Year, today.Month);
        Assert.All(holidays, h => Assert.Equal(today.Month, h.Month));
    }
}
