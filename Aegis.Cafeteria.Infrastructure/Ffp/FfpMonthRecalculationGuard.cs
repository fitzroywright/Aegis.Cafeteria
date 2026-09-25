namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Data.SqlClient;

public sealed class FfpMonthRecalculationGuard(FfpSqlConnectionFactory connections) : IMonthRecalculationGuard
{
    public async Task<MonthRecalculationGuardResult> CheckAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        DateTime start = new(year, month, 1);
        DateTime end = start.AddMonths(1);

        const string sql = @"
SELECT COUNT(*)
FROM dbo.EmployeeIDScans
WHERE ScanDate >= @Start
  AND ScanDate < @End
  AND IDName IN
  (
        'Cafeteria Purchase'
      , 'Cafeteria Refund'
      , 'Cafeteria Prior Period Refund'
      , 'Customer Payment'
      , 'Additional Credit'
      , 'Stipend Card Reduction'
  );";

        await using SqlConnection connection = await connections.OpenReadAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@Start", start);
        command.Parameters.AddWithValue("@End", end);

        int activityCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

        return activityCount == 0
            ? new MonthRecalculationGuardResult(true, 0, null)
            : new MonthRecalculationGuardResult(
                  false
                , activityCount
                , $"Month {year:D4}-{month:D2} contains {activityCount} cafeteria financial transaction(s). Recalculation is blocked once financial activity exists.");
    }
}
