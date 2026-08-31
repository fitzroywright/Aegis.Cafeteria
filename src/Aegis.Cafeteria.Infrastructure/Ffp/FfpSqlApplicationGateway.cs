namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapter for the existing FFP schema used by FFP.LunchSystem. the external system idempotency key
/// is the idempotency key. The scan insert and Employees summary update occur in one SQL transaction.
/// </summary>
public sealed class FfpSqlApplicationGateway(FfpIntegrationOptions options, ILogger<FfpSqlApplicationGateway> logger) :
      IExternalAccountingGateway
    , IWorkforceReferenceSource
    , IFundingPolicyCompatibilityGateway
    , IMonthRecalculationGuard
    , IDevelopmentDataResetGateway
{
    // Only employees eligible for the Cafeteria program are loaded into the POS workforce cache.
    private const string EmployeeSql = @"
SELECT
      e.ID
    , e.Code
    , e.TRN
    , e.NFCID
    , ISNULL(e.FirstName, '') + ' ' + ISNULL(e.LastName, '') AS Name
    , e.Photo
    , CAST(CASE WHEN e.EligibleForLunchTicket = 1 THEN 1 ELSE 0 END AS bit) AS IsEligible
    , ISNULL(d.Name, '') AS DepartmentName
FROM dbo.Employees AS e
LEFT OUTER JOIN dbo.Departments AS d ON d.ID = e.DepartmentID
WHERE e.EligibleForLunchTicket = 1;";

    private const string HolidaySql = @"
SELECT Name, CalendarDate
FROM dbo.Holidays
WHERE YEAR(CalendarDate) = @year;";

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var result = new List<ExternalCustomerSnapshot>();
        await using var cn = new SqlConnection(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(EmployeeSql, cn);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            int employeeId = Convert.ToInt32(r.GetValue(0));
            string? code = NormalizeIdentifier(r, 1);
            string? trn = NormalizeIdentifier(r, 2);
            string? nfc = NormalizeIdentifier(r, 3);
            var name = Convert.ToString(r.GetValue(4))?.Trim() ?? string.Empty;
            var photo = r.IsDBNull(5) ? null : (byte[])r.GetValue(5);
            var isEligible = !r.IsDBNull(6) && Convert.ToBoolean(r.GetValue(6));
            var department = r.IsDBNull(7) ? string.Empty : Convert.ToString(r.GetValue(7))?.Trim() ?? string.Empty;

            result.Add(new ExternalCustomerSnapshot(employeeId, name, isEligible, nfc, code, trn, photo, department));
        }
        return result;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        // FFP.Holidays is the source of truth. Fetch the year (same semantics as the supplied
        // YEAR(CalendarDate)=YEAR(GETDATE()) query) and return only the requested month.
        var list = new List<DateOnly>();
        await using var cn = new SqlConnection(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(HolidaySql, cn);
        cmd.Parameters.AddWithValue("@year", year);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var date = DateOnly.FromDateTime(r.GetDateTime(1));
            if (date.Month == month) list.Add(date);
        }
        return list.Distinct().ToArray();
    }


    public async Task MirrorFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , CancellationToken cancellationToken = default
    )
    {
        EnsureEnabled();
        EnsureWritesEnabled();

        const string sql = @"
UPDATE dbo.LunchItems
   SET Price = @Price,
       Cost = @Cost
 WHERE Code = 'FFP00';

IF @@ROWCOUNT <> 1
    THROW 51000, 'FFP00 compatibility policy row was not found or was not unique.', 1;";

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@Price", dailyCreditLimitRate);
        command.Parameters.AddWithValue("@Cost", dailyCompanySubsidyRate);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task<MonthRecalculationGuardResult> CheckAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        EnsureEnabled();

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

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@Start", start);
        command.Parameters.AddWithValue("@End", end);

        int activityCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

        return activityCount == 0
            ? new MonthRecalculationGuardResult(true, 0, null)
            : new MonthRecalculationGuardResult
            (
                  false
                , activityCount
                , $"Month {year:D4}-{month:D2} contains {activityCount} cafeteria financial transaction(s). Recalculation is blocked once financial activity exists."
            );
    }


    public async Task<DevelopmentDataResetResult> ResetAsync
    (
          string resetBy
        , DateTimeOffset resetAt
        , CancellationToken cancellationToken = default
    )
    {
        EnsureEnabled();
        EnsureWritesEnabled();

        const string sql = @"
DELETE FROM dbo.EmployeeIDScans
WHERE IDName IN
(
      'Tickets Assigned'
    , 'Monthly Allocation Recalculation'
    , 'Cafeteria Purchase'
    , 'Cafeteria Refund'
    , 'Cafeteria Prior Period Refund'
    , 'Customer Payment'
    , 'Additional Credit'
    , 'Stipend Card Reduction'
);

DECLARE @DeletedLedgerEntries int = @@ROWCOUNT;

UPDATE dbo.Employees
   SET CreditLimit = 0,
       AvailableBalance = 0
 WHERE EligibleForLunchTicket = 1;

SELECT @DeletedLedgerEntries, @@ROWCOUNT;";

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            DevelopmentDataResetResult result;

            await using (SqlCommand command = new(sql, connection, transaction))
            {
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The FFP development reset did not return a result.");
                }

                result = new DevelopmentDataResetResult
                (
                      reader.GetInt32(0)
                    , reader.GetInt32(1)
                );
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning
            (
                  "Reset Aegis.Cafeteria development data in FFP by {Actor} at {ResetAt}: deleted {LedgerCount} ledger rows and reset {EmployeeCount} employee accounts"
                , resetBy
                , resetAt
                , result.DeletedLedgerEntries
                , result.ResetEmployeeAccounts
            );

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        const string sql = "SELECT ID, ISNULL(CreditLimit,0), ISNULL(AvailableBalance,0) FROM Employees WHERE EligibleForLunchTicket = 1;";
        var result = new List<ExternalAccountSummary>();
        await using var cn = new SqlConnection(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ExternalAccountSummary(Convert.ToInt32(reader.GetValue(0)), Convert.ToDecimal(reader.GetValue(1)), Convert.ToDecimal(reader.GetValue(2))));
        return result;
    }

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        EnsureWritesEnabled();
        await using var cn = new SqlConnection(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM EmployeeIDScans WITH (UPDLOCK, HOLDLOCK) WHERE TransmissionID = @TransmissionID)
BEGIN
    INSERT INTO EmployeeIDScans
      (Code, Name, [Description], UserName, DateCreated, DateUpdated, ScanCode, ScanDate, ScanType, ScanValue, IDName, EmployeeID, TransmissionID)
    SELECT Code, Name, @Description, @UserName, @OccurredAt, @OccurredAt,
           @ScanCode, @OccurredAt, @ScanType, @ScanValue, @IDName, ID, @TransmissionID
    FROM Employees WHERE ID = @EmployeeID;

    IF @@ROWCOUNT = 0 THROW 51000, 'FFP employee not found for cafeteria ledger publication.', 1;

    UPDATE Employees
       SET CreditLimit = @CreditLimit,
           AvailableBalance = @AvailableBalance
     WHERE ID = @EmployeeID;
END", cn, tx);
            cmd.Parameters.AddWithValue("@TransmissionID", entry.OperationId.ToString());
            cmd.Parameters.AddWithValue("@EmployeeID", entry.CustomerId);
            cmd.Parameters.AddWithValue("@Description", entry.Description);
            cmd.Parameters.AddWithValue("@OccurredAt", entry.OccurredAt.LocalDateTime);
            cmd.Parameters.AddWithValue("@ScanCode", entry.ScanCode);
            cmd.Parameters.AddWithValue("@UserName", string.IsNullOrWhiteSpace(entry.UserName) ? "SYSTEM" : entry.UserName);
            cmd.Parameters.AddWithValue("@IDName", string.IsNullOrWhiteSpace(entry.IdName) ? entry.Description : entry.IdName);
            cmd.Parameters.AddWithValue("@ScanType", ScanType(entry.Kind));
            cmd.Parameters.AddWithValue("@ScanValue", entry.ScanValue);
            cmd.Parameters.AddWithValue("@CreditLimit", entry.CreditLimit);
            cmd.Parameters.AddWithValue("@AvailableBalance", entry.AvailableBalance);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            logger.LogInformation("Published FFP ledger entry {OperationId} for employee {EmployeeId}: {Kind} {ScanValue}; balance {AvailableBalance}",
                entry.OperationId, entry.CustomerId, entry.Kind, entry.ScanValue, entry.AvailableBalance);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private int ScanType(ExternalLedgerEntryKind kind) => kind switch
    {
        ExternalLedgerEntryKind.MonthlyAllocation => options.MonthlyAllocationScanType,
        ExternalLedgerEntryKind.Purchase => options.PurchaseScanType,
        ExternalLedgerEntryKind.Refund => options.RefundScanType,
        _ => options.AdjustmentScanType
    };

    private static string? NormalizeIdentifier(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = Convert.ToString(reader.GetValue(ordinal))?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
    }

    private void EnsureWritesEnabled()
    {
        if (!options.AllowWrites)
        {
            throw new InvalidOperationException("FFP writes are disabled for this environment. The configured FFP database is read-only from Aegis.Cafeteria.Services.");
        }
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled) throw new InvalidOperationException("FFP integration is disabled.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new InvalidOperationException("FFP database connection string is not configured.");
    }
}
