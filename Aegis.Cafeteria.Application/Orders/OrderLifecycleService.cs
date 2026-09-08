namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class OrderLifecycleService
{
    private readonly PurchaseAuthorizationService purchaseAuthorizationService;
    private readonly IOrderVoidAuthorizer voidAuthorizer;

    public OrderLifecycleService(
        PurchaseAuthorizationService purchaseAuthorizationService,
        IOrderVoidAuthorizer voidAuthorizer)
    {
        this.purchaseAuthorizationService = purchaseAuthorizationService;
        this.voidAuthorizer = voidAuthorizer;
    }

    public Order Create(int customerId, DateTimeOffset createdAt)
        => new(Guid.NewGuid(), customerId, createdAt);

    public void Submit(Order order, DateTimeOffset submittedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.Submit(submittedAt);
    }

    public void Complete(
        Customer customer,
        CustomerAccountSnapshot account,
        Order order,
        IEnumerable<OrderPayment> payments,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payments);

        var paymentArray = payments.ToArray();
        var accountAmount = paymentArray
            .Where(x => x.Method == PaymentMethod.CafeteriaAccount)
            .Sum(x => x.Amount);

        this.purchaseAuthorizationService.EnsureCanChargeAccount(customer, account, order, accountAmount);
        order.Complete(paymentArray, completedAt);
    }

    public void Void(Order order, string voidedBy, DateTimeOffset voidedAt, string reason)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!this.voidAuthorizer.CanVoid(voidedBy))
            throw new UnauthorizedAccessException("The user is not authorized to void orders.");
        order.Void(voidedBy, voidedAt, reason);
    }
}
