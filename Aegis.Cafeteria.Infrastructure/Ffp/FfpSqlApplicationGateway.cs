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
    private readonly FfpEmployeeWorkforceSource workforceSource = new(options);

    public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
        => this.workforceSource.GetCustomersAsync(cancellationToken);

    public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
        => this.workforceSource.GetHolidaysAsync(year, month, cancellationToken);

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
   SET Price = @Price
 WHERE Code = @CreditLimitCode;

IF @@ROWCOUNT <> 1
    THROW 51000, 'Configured credit-limit compatibility policy row was not found or was not unique.', 1;

UPDATE dbo.LunchItems
   SET Cost = @Cost
 WHERE Code = @SubsidyCode;

IF @@ROWCOUNT <> 1
    THROW 51000, 'Configured subsidy compatibility policy row was not found or was not unique.', 1;";

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@Price", dailyCreditLimitRate);
        command.Parameters.AddWithValue("@Cost", dailyCompanySubsidyRate);
        command.Parameters.AddWithValue("@CreditLimitCode", options.CreditLimitLunchItemCode);
        command.Parameters.AddWithValue("@SubsidyCode", options.SubsidyLunchItemCode);
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

        int timeoutSeconds = Math.Clamp(options.PublicationTimeoutSeconds, 15, 600);
        using CancellationTokenSource publicationTimeout = new(TimeSpan.FromSeconds(timeoutSeconds));
        CancellationToken durableToken = publicationTimeout.Token;

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(durableToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(durableToken);
        try
        {
            await using SqlCommand command = new(@"
SET XACT_ABORT ON;
IF NOT EXISTS (SELECT 1 FROM EmployeeIDScans WITH (UPDLOCK, HOLDLOCK) WHERE TransmissionID = @TransmissionID)
BEGIN
    INSERT INTO EmployeeIDScans
      (Code, Name, [Description], UserName, DateCreated, DateUpdated, ScanCode, ScanDate, ScanType, ScanValue, IDName, EmployeeID, TransmissionID, CreatedBy)
    SELECT CASE WHEN @UseIdentificationCode = 1 THEN @IdentificationCode ELSE Code END,
           Name, @Description, @UserName, @OccurredAt, @OccurredAt,
           @ScanCode, @OccurredAt, @ScanType, @ScanValue, @IDName, ID, @TransmissionID, @CreatedBy
    FROM Employees WHERE ID = @EmployeeID;

    IF @@ROWCOUNT = 0 THROW 51000, 'FFP employee not found for cafeteria ledger publication.', 1;

    UPDATE Employees
       SET CreditLimit = @CreditLimit,
           AvailableBalance = @AvailableBalance
     WHERE ID = @EmployeeID;
END", connection, transaction)
            {
                CommandTimeout = timeoutSeconds
            };
            command.Parameters.AddWithValue("@TransmissionID", entry.OperationId.ToString());
            command.Parameters.AddWithValue("@EmployeeID", entry.CustomerId);
            command.Parameters.AddWithValue("@Description", entry.Description);
            command.Parameters.AddWithValue("@OccurredAt", entry.OccurredAt.LocalDateTime);
            command.Parameters.AddWithValue("@ScanCode", entry.ScanCode);
            command.Parameters.AddWithValue("@UserName", string.IsNullOrWhiteSpace(entry.UserName) ? "SYSTEM" : entry.UserName);
            command.Parameters.AddWithValue("@IDName", string.IsNullOrWhiteSpace(entry.IdName) ? entry.Description : entry.IdName);
            bool useIdentificationCode = entry.Kind == ExternalLedgerEntryKind.Purchase && !string.IsNullOrWhiteSpace(entry.Code);
            command.Parameters.AddWithValue("@UseIdentificationCode", useIdentificationCode ? 1 : 0);
            command.Parameters.AddWithValue("@IdentificationCode", (object?)entry.Code ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatedBy", string.IsNullOrWhiteSpace(entry.CreatedBy) ? "SYSTEM" : entry.CreatedBy);
            command.Parameters.AddWithValue("@ScanType", ScanType(entry.Kind));
            command.Parameters.AddWithValue("@ScanValue", entry.ScanValue);
            command.Parameters.AddWithValue("@CreditLimit", entry.CreditLimit);
            command.Parameters.AddWithValue("@AvailableBalance", entry.AvailableBalance);
            await command.ExecuteNonQueryAsync(durableToken);
            await transaction.CommitAsync(durableToken);
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
        catch (SqlException exception) when (publicationTimeout.IsCancellationRequested)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException, "Rollback failed for timed-out FFP ledger entry {OperationId}", entry.OperationId);
            }

            logger.LogError
            (
                  exception
                , "FFP ledger publication {OperationId} for employee {EmployeeId} exceeded {TimeoutSeconds}s. SQL Number={SqlNumber}; State={SqlState}; Procedure={SqlProcedure}; Line={SqlLine}"
                , entry.OperationId
                , entry.CustomerId
                , timeoutSeconds
                , exception.Number
                , exception.State
                , exception.Procedure
                , exception.LineNumber
            );

            throw new TimeoutException
            (
                $"FFP ledger publication exceeded {timeoutSeconds} seconds. Check SQL Server blocking/locks and the EmployeeIDScans TransmissionID lookup.",
                exception
            );
        }
        catch (SqlException exception)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException, "Rollback failed for FFP ledger entry {OperationId}", entry.OperationId);
            }

            logger.LogError
            (
                  exception
                , "FFP SQL error publishing ledger entry {OperationId} for employee {EmployeeId}. SQL Number={SqlNumber}; State={SqlState}; Procedure={SqlProcedure}; Line={SqlLine}"
                , entry.OperationId
                , entry.CustomerId
                , exception.Number
                , exception.State
                , exception.Procedure
                , exception.LineNumber
            );
            throw;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(rollbackException, "Rollback failed for FFP ledger entry {OperationId}", entry.OperationId);
            }
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
}
