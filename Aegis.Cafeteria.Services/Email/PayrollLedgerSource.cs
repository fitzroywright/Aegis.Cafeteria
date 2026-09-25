namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Reporting;

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

public sealed class CafeteriaPayrollLedgerSource(
    CafeteriaLedgerQuery ledger) : IPayrollLedgerSource
{
    public async Task<IReadOnlyDictionary<int, PayrollLedgerTotals>> GetMonthlyTotalsAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<CafeteriaLedgerLine> lines =
            await ledger.GetMonthAsync(year, month, cancellationToken);

        Dictionary<int, PayrollLedgerTotals> result = [];

        foreach (IGrouping<int, CafeteriaLedgerLine> group in lines.GroupBy(line => line.CustomerId))
        {
            decimal purchases = group
                .Where(line => line.Kind == ExternalLedgerEntryKind.Purchase)
                .Sum(line => Math.Abs(line.Amount));

            decimal refunds = group
                .Where(line => line.Kind == ExternalLedgerEntryKind.Refund)
                .Sum(line => Math.Abs(line.Amount));

            decimal customerPayments = group
                .Where(line => line.Kind == ExternalLedgerEntryKind.CustomerPayment)
                .Sum(line => Math.Abs(line.Amount));

            decimal stipendCardReduction = group
                .Where(line => line.Kind == ExternalLedgerEntryKind.StipendCardReduction)
                .Sum(line => Math.Abs(line.Amount));

            result[group.Key] = new PayrollLedgerTotals(
                purchases,
                refunds,
                customerPayments,
                stipendCardReduction);
        }

        return result;
    }
}
