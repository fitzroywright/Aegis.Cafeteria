namespace Aegis.Cafeteria.Application.Abstractions;

public sealed record RecentTransactionView(
    Guid OrderId,
    DateTimeOffset CompletedAt,
    string CustomerName,
    decimal Total,
    decimal CafeteriaAccountAmount,
    decimal CashAmount,
    string TenderSummary,
    string CashierUserId,
    string TerminalId);

public interface IRecentTransactionQuery
{
    Task<IReadOnlyList<RecentTransactionView>> GetTodayAsync(
        int take = 10,
        DateOnly? localDate = null,
        CancellationToken cancellationToken = default);
}
