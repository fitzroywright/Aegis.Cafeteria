namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Application.Reporting;

public interface ICustomerAccountQuery
{
    Task<CustomerAccountView?> GetAsync(int customerId, int year, int month, CancellationToken cancellationToken = default);

    Task<CustomerAccountView?> GetSummaryAsync(
        int customerId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
        int customerId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task<CustomerAccountView?> GetCurrentAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return GetAsync(customerId, now.Year, now.Month, cancellationToken);
    }

    Task<CustomerAccountView?> GetCurrentSummaryAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return GetSummaryAsync(customerId, now.Year, now.Month, cancellationToken);
    }
}
