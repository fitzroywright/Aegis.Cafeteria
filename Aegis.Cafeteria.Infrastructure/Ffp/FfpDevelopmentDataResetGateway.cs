namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

public sealed class FfpDevelopmentDataResetGateway(
    FfpSqlConnectionFactory connections,
    ILogger<FfpDevelopmentDataResetGateway> logger) : IDevelopmentDataResetGateway
{
    public async Task<DevelopmentDataResetResult> ResetAsync(
        string resetBy,
        DateTimeOffset resetAt,
        CancellationToken cancellationToken = default)
    {
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

        await using SqlConnection connection = await connections.OpenWriteAsync(cancellationToken);
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

                result = new DevelopmentDataResetResult(reader.GetInt32(0), reader.GetInt32(1));
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogWarning(
                "Reset Aegis.Cafeteria development data in FFP by {Actor} at {ResetAt}: deleted {LedgerCount} ledger rows and reset {EmployeeCount} employee accounts",
                resetBy,
                resetAt,
                result.DeletedLedgerEntries,
                result.ResetEmployeeAccounts);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
