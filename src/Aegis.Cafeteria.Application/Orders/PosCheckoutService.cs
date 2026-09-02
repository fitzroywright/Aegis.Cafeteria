namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;

public sealed class PosCheckoutService
{
    private readonly ICafeteriaPersistenceStore persistenceStore;
    private readonly IExternalLedgerProjectionService? externalProjection;
    private readonly IPostSaleObserver[] postSaleObservers;

    public PosCheckoutService
    (
          ICafeteriaPersistenceStore persistenceStore
        , IExternalLedgerProjectionService? externalProjection = null
        , IEnumerable<IPostSaleObserver>? postSaleObservers = null
    )
    {
        this.persistenceStore = persistenceStore;
        this.externalProjection = externalProjection;
        this.postSaleObservers = postSaleObservers?.ToArray() ?? [];
    }

    [Obsolete("Production callers should use PosCheckoutRequest.")]
    public Task<Order> CompleteAsync(
        CustomerAccountView account,
        IReadOnlyCollection<PosOrderLine> lines,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        CancellationToken cancellationToken = default)
        => CompleteAsync(
            new PosCheckoutRequest(
                account,
                lines,
                cafeteriaAccountAmount,
                cashAmount,
                0m,
                "legacy",
                Environment.MachineName),
            cancellationToken);

    [Obsolete("Production callers should use PosCheckoutRequest.")]
    public Task<Order> CompleteAsync(
        CustomerAccountView account,
        IReadOnlyCollection<PosOrderLine> lines,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        string cashierUserId,
        string terminalId,
        CancellationToken cancellationToken = default)
        => CompleteAsync(
            new PosCheckoutRequest(
                account,
                lines,
                cafeteriaAccountAmount,
                cashAmount,
                0m,
                cashierUserId,
                terminalId),
            cancellationToken);

    public async Task<Order> CompleteAsync(PosCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Account);
        ArgumentNullException.ThrowIfNull(request.Lines);

        CustomerAccountView account = request.Account;
        IReadOnlyCollection<PosOrderLine> lines = request.Lines;

        if (account.Period.IsClosed)
            throw new InvalidOperationException("Sales cannot be completed for a closed cafeteria period.");

        if (lines.Count == 0)
            throw new InvalidOperationException("At least one order item is required.");

        if (request.CafeteriaAccountAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CafeteriaAccountAmount));
        if (request.CashAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CashAmount));
        if (request.CreditAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CreditAmount));

        bool canUseEmployeeAccount = !account.Customer.IsCashCustomer && account.Customer.IsEligibleForCredit;
        if (!canUseEmployeeAccount && (request.CafeteriaAccountAmount > 0m || request.CreditAmount > 0m))
        {
            throw new InvalidOperationException(account.Customer.IsCashCustomer
                ? "Cash customer sales must be paid entirely in cash."
                : "This customer is not eligible for Employee Card or credit sales.");
        }

        if (string.IsNullOrWhiteSpace(request.CashierUserId))
            throw new ArgumentException("Authenticated cashier user ID is required.", nameof(request.CashierUserId));
        if (string.IsNullOrWhiteSpace(request.TerminalId))
            throw new ArgumentException("Terminal ID is required.", nameof(request.TerminalId));
        if (request.CafeteriaAccountAmount > account.FinancialStatus.TotalAvailableToSpend)
            throw new InvalidOperationException("The Employee Card portion exceeds the customer's Available to Spend.");

        DateTimeOffset now = DateTimeOffset.Now;
        Order order = new(Guid.NewGuid(), account.Customer.Id, now);
        foreach (PosOrderLine line in lines)
        {
            if (line.Quantity <= 0)
                throw new InvalidOperationException("Order quantities must be greater than zero.");

            order.AddItem(new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity));
        }

        order.Submit(now);
        List<OrderPayment> payments = [];
        if (request.CafeteriaAccountAmount > 0m)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, request.CafeteriaAccountAmount, now));
        if (request.CashAmount > 0m)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, request.CashAmount, now));
        if (request.CreditAmount > 0m)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.Credit, request.CreditAmount, now));

        if (payments.Sum(x => x.Amount) != order.Total)
            throw new InvalidOperationException("Employee Card, cash, and credit tender must equal the order total.");

        order.Complete(payments, now, request.CashierUserId, request.TerminalId);
        await this.persistenceStore.SaveOrderAsync(order, cancellationToken);

        if (this.externalProjection is not null && !account.Customer.IsCashCustomer)
            await this.externalProjection.PublishCompletedOrderAsync(account, order, cancellationToken);

        foreach (IPostSaleObserver observer in this.postSaleObservers)
        {
            try
            {
                await observer.OnSaleCompletedAsync(account, order, cancellationToken);
            }
            catch
            {
                // Post-sale convenience features must never turn a committed sale into a checkout failure.
            }
        }

        return order;
    }
}

public sealed record PosCheckoutRequest(
    CustomerAccountView Account,
    IReadOnlyCollection<PosOrderLine> Lines,
    decimal CafeteriaAccountAmount,
    decimal CashAmount,
    decimal CreditAmount,
    string CashierUserId,
    string TerminalId);

public sealed record PosOrderLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}
