using Aegis.Cafeteria.Application.Reporting;

namespace Aegis.Cafeteria.UI.Linux;

internal sealed record TransactionRow(CustomerMonthlyTransactionView Transaction)
{
    public override string ToString() => $"{Transaction.OccurredAt.LocalDateTime:g} | {Transaction.Kind} | {Transaction.Description} | {Transaction.Amount:C2} | Account {Transaction.CafeteriaAccountAmount:C2} | Cash {Transaction.CashAmount:C2}";
}
