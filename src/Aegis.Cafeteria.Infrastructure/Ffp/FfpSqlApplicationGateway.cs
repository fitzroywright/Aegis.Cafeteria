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
    , e.LoginName
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
        List<ExternalCustomerSnapshot> result = new();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using SqlCommand cmd = new(EmployeeSql, cn);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            int employeeId = Convert.ToInt32(r.GetValue(0));
            string? code = NormalizeIdentifier(r, 1);
            string? trn = NormalizeIdentifier(r, 2);
            string? nfc = NormalizeIdentifier(r, 3);
            string name = Convert.ToString(r.GetValue(4))?.Trim() ?? string.Empty;
            byte[]? photo = r.IsDBNull(5) ? null : (byte[])r.GetValue(5);
            bool isEligible = !r.IsDBNull(6) && Convert.ToBoolean(r.GetValue(6));
            string department = r.IsDBNull(7) ? string.Empty : Convert.ToString(r.GetValue(7))?.Trim() ?? string.Empty;
            string? loginName = NormalizeIdentifier(r, 8);

            result.Add(new ExternalCustomerSnapshot(employeeId, name, isEligible, nfc, code, trn, photo, department, LoginName: loginName));
        }
        return result;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        List<DateOnly> result = new();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using SqlCommand cmd = new(HolidaySql, cn);
        cmd.Parameters.AddWithValue("@year", year);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            DateTime date = Convert.ToDateTime(r.GetValue(1));
            if (date.Month == month) result.Add(DateOnly.FromDateTime(date));
        }
        return result;
    }

    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        Dictionary<int, ExternalAccountSummary> result = new();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        const string sql = "SELECT ID, CreditLimit, AvailableBalance FROM dbo.Employees WHERE EligibleForLunchTicket=1;";
        await using SqlCommand cmd = new(sql, cn);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            int id = Convert.ToInt32(r.GetValue(0));
            decimal creditLimit = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
            decimal available = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2));
            result[id] = new ExternalAccountSummary(id, creditLimit, available);
        }
        return result.Values.ToArray();
    }

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string duplicateSql = "SELECT COUNT(1) FROM dbo.EmployeeIDScans WHERE ExternalOperationId=@id;";
            await using (SqlCommand duplicate = new(duplicateSql, cn, tx))
            {
                duplicate.Parameters.AddWithValue("@id", entry.IdempotencyKey);
                try
                {
                    if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
                    {
                        await tx.CommitAsync(cancellationToken);
                        return;
                    }
                }
                catch (SqlException exception) when (exception.Number == 207)
                {
                    logger.LogWarning("FFP EmployeeIDScans.ExternalOperationId is missing; external idempotency cannot be enforced until the column is added.");
                }
            }

            const string insertSql = @"
INSERT INTO dbo.EmployeeIDScans(EmployeeID,ScanDate,ScanType,ScanValue,UserName,IDName,Description,ExternalOperationId)
VALUES(@employeeId,@scanDate,@scanType,@scanValue,@userName,@idName,@description,@externalOperationId);";
            await using (SqlCommand insert = new(insertSql, cn, tx))
            {
                insert.Parameters.AddWithValue("@employeeId", entry.EmployeeId);
                insert.Parameters.AddWithValue("@scanDate", entry.OccurredAt.LocalDateTime);
                insert.Parameters.AddWithValue("@scanType", ResolveScanType(entry.EntryType));
                insert.Parameters.AddWithValue("@scanValue", entry.Amount);
                insert.Parameters.AddWithValue("@userName", entry.UserName);
                insert.Parameters.AddWithValue("@idName", entry.IdName);
                insert.Parameters.AddWithValue("@description", entry.Description);
                insert.Parameters.AddWithValue("@externalOperationId", entry.IdempotencyKey);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            const string updateSql = @"
UPDATE dbo.Employees SET AvailableBalance=ISNULL(AvailableBalance,0)+@amount WHERE ID=@employeeId;";
            await using (SqlCommand update = new(updateSql, cn, tx))
            {
                update.Parameters.AddWithValue("@amount", entry.Amount);
                update.Parameters.AddWithValue("@employeeId", entry.EmployeeId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task PublishMonthlyAllocationAsync(ExternalMonthlyAllocation allocation, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using SqlTransaction tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            const string updateSql = "UPDATE dbo.Employees SET CreditLimit=@creditLimit, AvailableBalance=@available WHERE ID=@employeeId;";
            await using (SqlCommand update = new(updateSql, cn, tx))
            {
                update.Parameters.AddWithValue("@creditLimit", allocation.CreditLimit);
                update.Parameters.AddWithValue("@available", allocation.AvailableBalance);
                update.Parameters.AddWithValue("@employeeId", allocation.EmployeeId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> HasFinancialActivityAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        const string sql = @"
SELECT TOP 1 1 FROM dbo.EmployeeIDScans
WHERE YEAR(ScanDate)=@year AND MONTH(ScanDate)=@month;";
        await using SqlCommand cmd = new(sql, cn);
        cmd.Parameters.AddWithValue("@year", year);
        cmd.Parameters.AddWithValue("@month", month);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task ResetDevelopmentDataAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        await using SqlConnection cn = new(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        const string sql = "DELETE FROM dbo.EmployeeIDScans; UPDATE dbo.Employees SET CreditLimit=0, AvailableBalance=0;";
        await using SqlCommand cmd = new(sql, cn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("FFP integration is not configured.");
    }

    private int ResolveScanType(ExternalLedgerEntryType type)
    {
        return type switch
        {
            ExternalLedgerEntryType.MonthlyAllocation => options.MonthlyAllocationScanType,
            ExternalLedgerEntryType.Purchase => options.PurchaseScanType,
            ExternalLedgerEntryType.Refund => options.RefundScanType,
            ExternalLedgerEntryType.Adjustment => options.AdjustmentScanType,
            _ => options.AdjustmentScanType
        };
    }

    private static string? NormalizeIdentifier(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        string? value = Convert.ToString(reader.GetValue(ordinal))?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
