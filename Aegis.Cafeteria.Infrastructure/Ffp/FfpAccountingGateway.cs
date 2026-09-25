namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

public sealed class FfpAccountingGateway(
    FfpIntegrationOptions options,
    FfpSqlConnectionFactory connections,
    ILogger<FfpAccountingGateway> logger) : IExternalAccountingGateway
{
    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT ID, ISNULL(CreditLimit,0), ISNULL(AvailableBalance,0) FROM Employees WHERE EligibleForLunchTicket = 1;";
        List<ExternalAccountSummary> result = [];

        await using SqlConnection connection = await connections.OpenReadAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ExternalAccountSummary
            (
                  Convert.ToInt32(reader.GetValue(0))
                , Convert.ToDecimal(reader.GetValue(1))
                , Convert.ToDecimal(reader.GetValue(2))
            ));
        }

        return result;
    }

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        int timeoutSeconds = Math.Clamp(options.PublicationTimeoutSeconds, 15, 600);
        using CancellationTokenSource publicationTimeout = new(TimeSpan.FromSeconds(timeoutSeconds));
        CancellationToken durableToken = publicationTimeout.Token;

        await using SqlConnection connection = await connections.OpenWriteAsync(durableToken);
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

            logger.LogInformation(
                "Published FFP ledger entry {OperationId} for employee {EmployeeId}: {Kind} {ScanValue}; balance {AvailableBalance}",
                entry.OperationId,
                entry.CustomerId,
                entry.Kind,
                entry.ScanValue,
                entry.AvailableBalance);
        }
        catch (SqlException exception) when (publicationTimeout.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction, entry.OperationId);
            logger.LogError(
                exception,
                "FFP ledger publication {OperationId} for employee {EmployeeId} exceeded {TimeoutSeconds}s. SQL Number={SqlNumber}; State={SqlState}; Procedure={SqlProcedure}; Line={SqlLine}",
                entry.OperationId,
                entry.CustomerId,
                timeoutSeconds,
                exception.Number,
                exception.State,
                exception.Procedure,
                exception.LineNumber);

            throw new TimeoutException(
                $"FFP ledger publication exceeded {timeoutSeconds} seconds. Check SQL Server blocking/locks and the EmployeeIDScans TransmissionID lookup.",
                exception);
        }
        catch (SqlException exception)
        {
            await TryRollbackAsync(transaction, entry.OperationId);
            logger.LogError(
                exception,
                "FFP SQL error publishing ledger entry {OperationId} for employee {EmployeeId}. SQL Number={SqlNumber}; State={SqlState}; Procedure={SqlProcedure}; Line={SqlLine}",
                entry.OperationId,
                entry.CustomerId,
                exception.Number,
                exception.State,
                exception.Procedure,
                exception.LineNumber);
            throw;
        }
        catch
        {
            await TryRollbackAsync(transaction, entry.OperationId);
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

    private async Task TryRollbackAsync(SqlTransaction transaction, Guid operationId)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(rollbackException, "Rollback failed for FFP ledger entry {OperationId}", operationId);
        }
    }
}
