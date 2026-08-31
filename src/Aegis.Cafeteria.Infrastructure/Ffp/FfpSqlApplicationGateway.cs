namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapter for the existing FFP schema used by FFP.LunchSystem. the external system idempotency key
/// is the idempotency key. The scan insert and Employees summary update occur in one SQL transaction.
/// </summary>
public sealed class FfpSqlApplicationGateway(FfpIntegrationOptions options, ILogger<FfpSqlApplicationGateway> logger) : IExternalAccountingGateway, IWorkforceReferenceSource, IProductCatalogSource
{
    // Ported from FFP.LunchSystem SQLConstants.GetEmployeeListSQL/SystemFunctions.GetData.
    // We intentionally load all employees (rather than only eligible employees) so Aegis can
    // identify the person first and then enforce EligibleForLunchTicket explicitly.
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
LEFT OUTER JOIN dbo.Departments AS d ON d.ID = e.DepartmentID;";

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
            var employeeId = Convert.ToInt32(r.GetValue(0));
            var idFallback = employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Preserve FFP.LunchSystem identifier fallback for the three supported identifiers: Code, TRN and NFC.
            var code = NormalizeIdentifier(r, 1) ?? idFallback;
            var trn = NormalizeIdentifier(r, 2) ?? idFallback;
            var nfc = NormalizeIdentifier(r, 3) ?? idFallback;
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

    public async Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        const string sql = @"
SELECT ID, Code, Name, Price, Cost
FROM dbo.LunchItems
WHERE IsActive = 1
ORDER BY Name, ID;";

        var activeLunchItems = new List<ExternalProductSnapshot>();
        await using var cn = new SqlConnection(options.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var id = Convert.ToInt32(r.GetValue(0));
            var code = r.IsDBNull(1) ? string.Empty : Convert.ToString(r.GetValue(1))?.Trim() ?? string.Empty;
            var name = r.IsDBNull(2) ? string.Empty : Convert.ToString(r.GetValue(2))?.Trim() ?? string.Empty;
            // Price is the employee-facing credit/sale value; Cost is the company subsidy value.
            var price = r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetValue(3), System.Globalization.CultureInfo.InvariantCulture);
            var cost = r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetValue(4), System.Globalization.CultureInfo.InvariantCulture);
            activeLunchItems.Add(new ExternalProductSnapshot(id, code, name, price, cost));
        }

        return LunchItemRatePolicyDeriver.Derive(activeLunchItems, DateTimeOffset.UtcNow, options.CreditLimitLunchItemCode, options.SubsidyLunchItemCode);
    }

    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        const string sql = "SELECT ID, ISNULL(CreditLimit,0), ISNULL(AvailableBalance,0) FROM Employees;";
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

    private void EnsureEnabled()
    {
        if (!options.Enabled) throw new InvalidOperationException("FFP integration is disabled.");
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new InvalidOperationException("FFP database connection string is not configured.");
    }
}
