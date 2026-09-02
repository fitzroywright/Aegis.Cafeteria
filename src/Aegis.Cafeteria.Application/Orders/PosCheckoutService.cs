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

    public PosCheckoutService(
        ICafeteriaPersistenceStore persistenceStore,
        IExternalLedgerProjectionService? externalProjection = null,
        IEnumerable<IPostSaleObserver>? postSaleObservers = null)
    {
        this.persistenceStore = persistenceStore;
        this.externalProjection = externalProjection;
        this.postSaleObservers = postSaleObservers?.ToArray() ?? [];
    }

    public int PostSaleObserverCount => this.postSaleObservers.Length;

    public IReadOnlyList<string> PostSaleObserverNames => this.postSaleObservers
        .Select(observer => observer.GetType().FullName ?? observer.GetType().Name)
        .ToArray();

    public static string DiagnosticLogPath => Path.Combine(Path.GetTempPath(), "aegis-cafeteria-postsale.log");

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

        if (account.Period.IsClosed)
        {
            throw new InvalidOperationException("Sales cannot be completed for a closed cafeteria period.");
        }

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

        DateTimeOffset now = DateTimeOffset.Now;
        Order order = new(Guid.NewGuid(), account.Customer.Id, now);
        foreach (PosOrderLine line in lines)
        {
            if (line.Quantity <= 0) throw new InvalidOperationException("Order quantities must be greater than zero.");
            order.AddItem(new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Quantity));
        }

        order.Submit(now);
        List<OrderPayment> payments = [];
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

        WriteDiagnostic($"SALE COMMITTED order={order.Id} customer={account.Customer.Id} cashCustomer={account.Customer.IsCashCustomer} eligible={account.Customer.IsEligibleForCredit} observers={this.postSaleObservers.Length} observerNames=[{string.Join(", ", PostSaleObserverNames)}]");

        foreach (IPostSaleObserver observer in this.postSaleObservers)
        {
            string observerName = observer.GetType().FullName ?? observer.GetType().Name;
            WriteDiagnostic($"OBSERVER ENTER {observerName} order={order.Id} customer={account.Customer.Id}");
            try
            {
                await observer.OnSaleCompletedAsync(account, order, cancellationToken);
                WriteDiagnostic($"OBSERVER SUCCESS {observerName} order={order.Id} customer={account.Customer.Id}");
            }
            catch (Exception exception)
            {
                WriteDiagnostic($"OBSERVER FAILURE {observerName} order={order.Id} customer={account.Customer.Id} exception={exception.GetType().FullName} message={exception.GetBaseException().Message}");
                // A post-sale convenience feature must never turn a committed sale into a checkout failure.
            }
        }

        return order;
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            string line = $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}";
            File.AppendAllText(DiagnosticLogPath, line);
        }
        catch
        {
            // Diagnostics must never interfere with a sale.
        }
    }
}

public sealed record PosOrderLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}
