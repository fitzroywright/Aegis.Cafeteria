namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Reporting;

public sealed record DailyLedgerSummary(
    int Transactions,
    decimal Purchases,
    decimal Refunds,
    decimal AdditionalCredit,
    decimal StipendReductions);

public sealed record EmployeeWeeklyLedger(
    int EmployeeId,
    string EmployeeName,
    string Email,
    IReadOnlyList<EmployeeWeeklyLedgerLine> Lines);

public sealed record EmployeeWeeklyLedgerLine(
    DateTime ScanDate,
    string IdName,
    string Description,
    decimal ScanValue);

public sealed class CafeteriaEmailReportSource(
    CafeteriaLedgerQuery ledger,
    IWorkforceReferenceSource workforce)
{
    public async Task<DailyLedgerSummary> GetDailySummaryAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset from = new(
            date.Year,
            date.Month,
            date.Day,
            0,
            0,
            0,
            TimeSpan.Zero);

        IReadOnlyList<CafeteriaLedgerLine> lines = await ledger.GetAsync(
            from,
            from.AddDays(1),
            cancellationToken);

        CafeteriaLedgerLine[] financial = lines
            .Where(IsDailyFinancialLine)
            .ToArray();

        return new DailyLedgerSummary(
            financial.Length,
            financial
                .Where(line => line.Kind == ExternalLedgerEntryKind.Purchase)
                .Sum(line => Math.Abs(line.Amount)),
            financial
                .Where(line => line.Kind == ExternalLedgerEntryKind.Refund)
                .Sum(line => Math.Abs(line.Amount)),
            financial
                .Where(line => line.Kind == ExternalLedgerEntryKind.AdditionalCredit)
                .Sum(line => Math.Abs(line.Amount)),
            financial
                .Where(line => line.Kind == ExternalLedgerEntryKind.StipendCardReduction)
                .Sum(line => Math.Abs(line.Amount)));
    }

    public async Task<IReadOnlyList<EmployeeWeeklyLedger>> GetWeeklyEmployeeLedgersAsync(
        DateOnly weekStart,
        DateOnly weekEnd,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset from = new(
            weekStart.Year,
            weekStart.Month,
            weekStart.Day,
            0,
            0,
            0,
            TimeSpan.Zero);

        DateOnly exclusiveEnd = weekEnd.AddDays(1);
        DateTimeOffset to = new(
            exclusiveEnd.Year,
            exclusiveEnd.Month,
            exclusiveEnd.Day,
            0,
            0,
            0,
            TimeSpan.Zero);

        IReadOnlyList<CafeteriaLedgerLine> lines =
            await ledger.GetAsync(from, to, cancellationToken);

        IReadOnlyList<ExternalCustomerSnapshot> employees =
            await workforce.GetCustomersAsync(cancellationToken);

        Dictionary<int, string> names = employees.ToDictionary(
            employee => employee.CustomerId,
            employee => employee.Name);

        return lines
            .GroupBy(line => line.CustomerId)
            .OrderBy(group => names.GetValueOrDefault(group.Key, string.Empty))
            .ThenBy(group => group.Key)
            .Select(group => new EmployeeWeeklyLedger(
                group.Key,
                names.GetValueOrDefault(group.Key, $"Employee #{group.Key}"),
                string.Empty,
                group
                    .OrderBy(line => line.OccurredAt)
                    .Select(line => new EmployeeWeeklyLedgerLine(
                        line.OccurredAt.LocalDateTime,
                        DisplayName(line.Kind),
                        line.Description,
                        line.Amount))
                    .ToArray()))
            .ToArray();
    }

    private static bool IsDailyFinancialLine(CafeteriaLedgerLine line)
        => line.Kind is
               ExternalLedgerEntryKind.Purchase
            or ExternalLedgerEntryKind.Refund
            or ExternalLedgerEntryKind.CustomerPayment
            or ExternalLedgerEntryKind.AdditionalCredit
            or ExternalLedgerEntryKind.StipendCardReduction;

    private static string DisplayName(ExternalLedgerEntryKind kind)
        => kind switch
        {
            ExternalLedgerEntryKind.MonthlyAllocation => "Monthly Allocation",
            ExternalLedgerEntryKind.Purchase => "Cafeteria Purchase",
            ExternalLedgerEntryKind.Refund => "Cafeteria Refund",
            ExternalLedgerEntryKind.CustomerPayment => "Customer Payment",
            ExternalLedgerEntryKind.AdditionalCredit => "Additional Credit",
            ExternalLedgerEntryKind.StipendCardReduction => "Stipend Card Reduction",
            ExternalLedgerEntryKind.Adjustment => "Adjustment",
            _ => kind.ToString()
        };
}
