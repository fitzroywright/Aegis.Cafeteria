namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Domain.Orders;

public interface IOrderQuery
{
    Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetRecentCompletedAsync(int? customerId = null, int take = 50, CancellationToken cancellationToken = default);
}
