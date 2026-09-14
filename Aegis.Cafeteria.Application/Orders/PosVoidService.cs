namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;

public sealed class PosVoidService(
    ICafeteriaPersistenceStore persistenceStore,
    IOrderVoidAuthorizer authorizer)
{
    public async Task<Order> VoidCurrentSaleAsync(
        CustomerAccountView account,
        IReadOnlyCollection<PosOrderLine> lines,
        string voidedBy,
        string terminalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw new InvalidOperationException("There is no current sale to void.");
        if (string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("Terminal ID is required.", nameof(terminalId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Void reason is required.", nameof(reason));
        if (!authorizer.CanVoid(voidedBy))
            throw new UnauthorizedAccessException("The user is not authorized to void sales.");

        var now = DateTimeOffset.Now;
        var order = new Order(Guid.NewGuid(), account.Customer.Id, now);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0) throw new InvalidOperationException("Order quantities must be greater than zero.");
            order.AddItem(new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity));
        }

        order.Submit(now);
        order.Void(voidedBy, now, $"[{terminalId.Trim()}] {reason.Trim()}");
        await persistenceStore.SaveOrderAsync(order, cancellationToken);
        return order;
    }
}
