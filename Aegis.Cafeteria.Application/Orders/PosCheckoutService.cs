namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Domain.Orders;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public sealed class PosCheckoutService
{
    private readonly ICafeteriaPersistenceStore persistenceStore;
    private readonly IPostSaleObserver[] postSaleObservers;
    private readonly IOperatorSession? operatorSession;
    private readonly ILogger<PosCheckoutService>? logger;

    public PosCheckoutService
    (
          ICafeteriaPersistenceStore persistenceStore
        , IEnumerable<IPostSaleObserver>? postSaleObservers = null
        , IOperatorSession? operatorSession = null
        , ILogger<PosCheckoutService>? logger = null
    )
    {
        this.persistenceStore = persistenceStore;
        this.postSaleObservers = postSaleObservers?.ToArray() ?? [];
        this.operatorSession = operatorSession;
        this.logger = logger;
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
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch step = Stopwatch.StartNew();
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
        Order order = new(
            Guid.NewGuid(),
            account.Customer.Id,
            now,
            account.Identification?.Method,
            account.Identification?.Code);
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

        string cashierName = this.operatorSession?.Current?.DisplayName?.Trim() ?? request.CashierUserId.Trim();
        order.Complete(payments, now, request.CashierUserId, request.TerminalId, cashierName);
        logger?.LogInformation("PERF Checkout OrderId={OrderId} validation/build {ElapsedMs} ms", order.Id, step.ElapsedMilliseconds);

        step.Restart();
        await this.persistenceStore.SaveOrderAsync(order, cancellationToken);
        logger?.LogInformation("PERF Checkout OrderId={OrderId} persistence {ElapsedMs} ms", order.Id, step.ElapsedMilliseconds);

        step.Restart();
        foreach (IPostSaleObserver observer in this.postSaleObservers)
        {
            _ = ObservePostSaleAsync(observer, account, order, cancellationToken);
        }
        logger?.LogInformation("PERF Checkout OrderId={OrderId} observer scheduling {ElapsedMs} ms Count={ObserverCount}", order.Id, step.ElapsedMilliseconds, this.postSaleObservers.Length);
        logger?.LogInformation("PERF Checkout OrderId={OrderId} TOTAL {ElapsedMs} ms", order.Id, total.ElapsedMilliseconds);

        return order;
    }
    private static async Task ObservePostSaleAsync(
        IPostSaleObserver observer,
        CustomerAccountView account,
        Order order,
        CancellationToken cancellationToken)
    {
        try
        {
            await observer.OnSaleCompletedAsync(account, order, cancellationToken);
        }
        catch
        {
            // Post-sale convenience features are deliberately detached from the
            // cashier critical path and must never turn a committed sale into a failure.
        }
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
