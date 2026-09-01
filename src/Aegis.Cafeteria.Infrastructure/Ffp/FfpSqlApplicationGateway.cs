namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.ActiveDirectory;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

/// <summary>
/// Service-side adapter for the existing FFP schema. POS terminals never connect directly to FFP SQL.
/// The external system idempotency key is the idempotency key. The scan insert and Employees summary
/// update occur in one SQL transaction.
/// </summary>
public sealed class FfpSqlApplicationGateway(FfpIntegrationOptions options, ILogger<FfpSqlApplicationGateway> logger) :
      IExternalAccountingGateway
    , IWorkforceReferenceSource
    , IFundingPolicyCompatibilityGateway
    , IMonthRecalculationGuard
    , IDevelopmentDataResetGateway
{
    private readonly ActiveDirectoryEmployeeDirectory directory = new();

    private const string EmployeeSql = @"
SELECT
      e.ID
    , e.Code
    , e.TRN
    , e.NFCID
    , ISNULL(e.FirstName, '') + ' ' + ISNULL(e.LastName, '') AS Name
    , e.Photo
    , CAST(CASE WHEN e.EligibleForLunchTicket = 1 THEN 1 ELSE 0 END AS bit) AS IsEligible
    , ISNULL(CONVERT(nvarchar(256), e.LoginName), '') AS LoginName
FROM dbo.Employees AS e
WHERE e.EligibleForLunchTicket = 1;";

    private const string HolidaySql = @"
SELECT Name, CalendarDate
FROM dbo.Holidays
WHERE YEAR(CalendarDate) = @year;";

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        List<EmployeeSourceRow> sourceRows = [];
        await using (SqlConnection connection = new(options.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = new(EmployeeSql, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                sourceRows.Add
                (
                    new EmployeeSourceRow
                    (
                          Convert.ToInt32(reader.GetValue(0))
                        , NormalizeIdentifier(reader, 1)
                        , NormalizeIdentifier(reader, 2)
                        , NormalizeIdentifier(reader, 3)
                        , Convert.ToString(reader.GetValue(4))?.Trim() ?? string.Empty
                        , reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5)
                        , !reader.IsDBNull(6) && Convert.ToBoolean(reader.GetValue(6))
                        , reader.IsDBNull(7) ? string.Empty : Convert.ToString(reader.GetValue(7))?.Trim() ?? string.Empty
                    )
                );
            }
        }

        IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> directoryRecords;
        try
        {
            directoryRecords = this.directory.GetByLoginNames(sourceRows.Select(employee => employee.LoginName));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning
            (
                  exception
                , "Active Directory employee metadata lookup failed in Aegis.Cafeteria.Services; employee records will be returned without AD metadata"
            );
            directoryRecords = new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
        }

        List<ExternalCustomerSnapshot> result = [];
        foreach (EmployeeSourceRow employee in sourceRows)
        {
            string normalizedLoginName = NormalizeLoginName(employee.LoginName);
            directoryRecords.TryGetValue(normalizedLoginName, out ActiveDirectoryEmployeeRecord? directoryEmployee);

            result.Add
            (
                new ExternalCustomerSnapshot
                (
                      employee.EmployeeId
                    , employee.Name
                    , employee.IsEligible
                    , employee.Nfc
                    , employee.Code
                    , employee.Trn
                    , employee.Photo
                    , directoryEmployee?.Department ?? string.Empty
                    , 0m
                    , directoryEmployee?.Manager ?? string.Empty
                    , directoryEmployee?.EmailAddress ?? string.Empty
                    , directoryEmployee?.Extension ?? string.Empty
                    , employee.LoginName
                )
            );
        }

        return result;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        List<DateOnly> dates = [];
        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(HolidaySql, connection);
        command.Parameters.AddWithValue("@year", year);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateOnly date = DateOnly.FromDateTime(reader.GetDateTime(1));
            if (date.Month == month)
            {
                dates.Add(date);
            }
        }
        return dates.Distinct().ToArray();
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
        List<ExternalAccountSummary> result = [];
        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add
            (
                new ExternalAccountSummary
                (
                      Convert.ToInt32(reader.GetValue(0))
                    , Convert.ToDecimal(reader.GetValue(1))
                    , Convert.ToDecimal(reader.GetValue(2))
                )
            );
        }
        return result;
    }

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        EnsureWritesEnabled();
        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using SqlCommand command = new(@"
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
END", connection, transaction);
            command.Parameters.AddWithValue("@TransmissionID", entry.OperationId.ToString());
            command.Parameters.AddWithValue("@EmployeeID", entry.CustomerId);
            command.Parameters.AddWithValue("@Description", entry.Description);
            command.Parameters.AddWithValue("@OccurredAt", entry.OccurredAt.LocalDateTime);
            command.Parameters.AddWithValue("@ScanCode", entry.ScanCode);
            command.Parameters.AddWithValue("@UserName", string.IsNullOrWhiteSpace(entry.UserName) ? "SYSTEM" : entry.UserName);
            command.Parameters.AddWithValue("@IDName", string.IsNullOrWhiteSpace(entry.IdName) ? entry.Description : entry.IdName);
            command.Parameters.AddWithValue("@ScanType", ScanType(entry.Kind));
            command.Parameters.AddWithValue("@ScanValue", entry.ScanValue);
            command.Parameters.AddWithValue("@CreditLimit", entry.CreditLimit);
            command.Parameters.AddWithValue("@AvailableBalance", entry.AvailableBalance);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation
            (
                  "Published FFP ledger entry {OperationId} for employee {EmployeeId}: {Kind} {ScanValue}; balance {AvailableBalance}"
                , entry.OperationId
                , entry.CustomerId
                , entry.Kind
                , entry.ScanValue
                , entry.AvailableBalance
            );
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
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
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        string? value = Convert.ToString(reader.GetValue(ordinal))?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
    }

    private static string NormalizeLoginName(string value)
    {
        string loginName = value.Trim();
        int slash = loginName.LastIndexOf('\\');
        if (slash >= 0 && slash < loginName.Length - 1)
        {
            loginName = loginName[(slash + 1)..];
        }

        int at = loginName.IndexOf('@');
        if (at > 0)
        {
            loginName = loginName[..at];
        }

        return loginName.Trim().ToLowerInvariant();
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
        if (!options.Enabled)
        {
            throw new InvalidOperationException("FFP integration is disabled.");
        }
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("FFP database connection string is not configured.");
        }
    }

    private sealed record EmployeeSourceRow
    (
          int EmployeeId
        , string? Code
        , string? Trn
        , string? Nfc
        , string Name
        , byte[]? Photo
        , bool IsEligible
        , string LoginName
    );
}
