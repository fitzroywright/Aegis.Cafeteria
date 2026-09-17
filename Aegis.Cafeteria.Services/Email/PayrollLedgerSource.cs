namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;

using Microsoft.Data.SqlClient;

using System.Globalization;

public sealed record PayrollLedgerTotals
(
      decimal Purchases
    , decimal Refunds
    , decimal CustomerPayments
    , decimal StipendCardReduction
)
{
    public static PayrollLedgerTotals Empty { get; } = new(0m, 0m, 0m, 0m);
}

public interface IPayrollLedgerSource
{
    Task<IReadOnlyDictionary<int, PayrollLedgerTotals>> GetMonthlyTotalsAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}

public sealed class FfpPayrollLedgerSource : IPayrollLedgerSource
{
    private readonly FfpIntegrationOptions ffp;

    public FfpPayrollLedgerSource(FfpIntegrationOptions ffp)
    {
        this.ffp = ffp;
    }

    public async Task<IReadOnlyDictionary<int, PayrollLedgerTotals>> GetMonthlyTotalsAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        DateTime from = new(year, month, 1);
        DateTime to = from.AddMonths(1);

        const string sql = @"
SELECT
      EmployeeID
    , SUM(CASE WHEN IDName = 'Cafeteria Purchase' THEN ISNULL(ScanValue, 0) ELSE 0 END) AS Purchases
    , SUM(CASE WHEN IDName = 'Cafeteria Refund' THEN ABS(ISNULL(ScanValue, 0)) ELSE 0 END) AS Refunds
    , SUM(CASE WHEN IDName = 'Customer Payment' THEN ISNULL(ScanValue, 0) ELSE 0 END) AS CustomerPayments
    , SUM(CASE WHEN IDName = 'Stipend Card Reduction' THEN ISNULL(ScanValue, 0) ELSE 0 END) AS StipendCardReduction
FROM dbo.EmployeeIDScans
WHERE ScanDate >= @from
  AND ScanDate < @to
  AND IDName IN ('Cafeteria Purchase', 'Cafeteria Refund', 'Customer Payment', 'Stipend Card Reduction')
GROUP BY EmployeeID;";

        Dictionary<int, PayrollLedgerTotals> result = [];

        await using SqlConnection connection = new(this.ffp.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            int employeeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);

            result[employeeId] = new PayrollLedgerTotals
            (
                  Decimal(reader, 1)
                , Decimal(reader, 2)
                , Decimal(reader, 3)
                , Decimal(reader, 4)
            );
        }

        return result;
    }

    private static decimal Decimal(SqlDataReader reader, int index)
    {
        return reader.IsDBNull(index)
            ? 0m
            : Convert.ToDecimal(reader.GetValue(index), CultureInfo.InvariantCulture);
    }
}
