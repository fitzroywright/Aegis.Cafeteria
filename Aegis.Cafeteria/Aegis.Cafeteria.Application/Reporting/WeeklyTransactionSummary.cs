namespace Aegis.Cafeteria.Application.Reporting;

public sealed record WeeklyTransactionSummary(
    int CustomerId,
    string CustomerName,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int TransactionCount,
    decimal TotalSales,
    decimal CafeteriaAccountSpending,
    decimal CashSpending,
    decimal Refunds,
    decimal EndingAvailableBalance);

public interface IWeeklyTransactionSummaryQuery
{
    Task<IReadOnlyList<WeeklyTransactionSummary>> GetAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default);
}

public interface IWeeklySummaryDispatchStore
{
    Task<bool> WasSentAsync(int customerId, DateOnly weekStart, CancellationToken cancellationToken = default);
    Task MarkSentAsync(int customerId, DateOnly weekStart, Guid messageId, DateTimeOffset sentAt, CancellationToken cancellationToken = default);
}
