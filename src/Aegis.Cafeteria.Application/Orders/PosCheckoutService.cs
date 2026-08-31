namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;

public sealed class PosCheckoutService
{
    private readonly ICafeteriaPersistenceStore persistenceStore;
    private readonly IExternalLedgerProjectionService? externalProjection;

    public PosCheckoutService(ICafeteriaPersistenceStore persistenceStore, IExternalLedgerProjectionService? externalProjection = null)
    {
        this.persistenceStore = persistenceStore;
        this.externalProjection = externalProjection;
    }

    [Obsolete("Production callers must supply the authenticated cashier user ID and terminal ID.")]
    public Task<Order> CompleteAsync(
        CustomerAccountView account,
        IReadOnlyCollection<PosOrderLine> lines,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        CancellationToken cancellationToken = default)
        => CompleteAsync(account, lines, cafeteriaAccountAmount, cashAmount, "legacy", Environment.MachineName, cancellationToken);

    public async Task<Order> CompleteAsync(
        CustomerAccountView account,
        IReadOnlyCollection<PosOrderLine> lines,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        string cashierUserId,
        string terminalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) throw new InvalidOperationException("At least one order item is required.");
        if ((account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit) && cafeteriaAccountAmount > 0m)
            throw new InvalidOperationException(account.Customer.IsCashCustomer
                ? "Cash customer sales must be paid entirely in cash."
                : "This customer is not eligible for cafeteria credit.");
        if (cafeteriaAccountAmount < 0m) throw new ArgumentOutOfRangeException(nameof(cafeteriaAccountAmount));
        if (cashAmount < 0m) throw new ArgumentOutOfRangeException(nameof(cashAmount));
        if (string.IsNullOrWhiteSpace(cashierUserId)) throw new ArgumentException("Authenticated cashier user ID is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("Terminal ID is required.", nameof(terminalId));
        if (cafeteriaAccountAmount > account.FinancialStatus.TotalAvailableToSpend)
            throw new InvalidOperationException("The cafeteria-account portion exceeds the customer's available credit.");

        var now = DateTimeOffset.Now;
        var order = new Order(Guid.NewGuid(), account.Customer.Id, now);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0) throw new InvalidOperationException("Order quantities must be greater than zero.");
            order.AddItem(new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity));
        }

        order.Submit(now);
        var payments = new List<OrderPayment>();
        if (cafeteriaAccountAmount > 0m)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, cafeteriaAccountAmount, now));
        if (cashAmount > 0m)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, cashAmount, now));

        if (payments.Sum(x => x.Amount) != order.Total)
            throw new InvalidOperationException("Cafeteria-account and cash tender must equal the order total.");

        order.Complete(payments, now, cashierUserId, terminalId);
        await persistenceStore.SaveOrderAsync(order, cancellationToken);
        if (externalProjection is not null && !account.Customer.IsCashCustomer)
            await externalProjection.PublishCompletedOrderAsync(account, order, cancellationToken);
        return order;
    }
}

public sealed record PosOrderLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}
