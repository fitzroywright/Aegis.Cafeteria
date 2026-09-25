namespace Aegis.Cafeteria.Services.Reporting;

using Aegis.Cafeteria.Application.Integration;

public sealed class CafeteriaMonthRecalculationGuard(
    CafeteriaLedgerQuery ledger) : IMonthRecalculationGuard
{
    public async Task<MonthRecalculationGuardResult> CheckAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > 9999)
            throw new ArgumentOutOfRangeException(nameof(year));

        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        IReadOnlyList<CafeteriaLedgerLine> lines =
            await ledger.GetMonthAsync(year, month, cancellationToken);

        int activityCount = lines.Count(line =>
            line.Kind is
                   ExternalLedgerEntryKind.Purchase
                or ExternalLedgerEntryKind.Refund
                or ExternalLedgerEntryKind.CustomerPayment
                or ExternalLedgerEntryKind.AdditionalCredit
                or ExternalLedgerEntryKind.StipendCardReduction);

        return activityCount == 0
            ? new MonthRecalculationGuardResult(true, 0, null)
            : new MonthRecalculationGuardResult(
                false,
                activityCount,
                $"Month {year:D4}-{month:D2} contains {activityCount} cafeteria financial transaction(s). Recalculation is blocked once financial activity exists.");
    }
}
