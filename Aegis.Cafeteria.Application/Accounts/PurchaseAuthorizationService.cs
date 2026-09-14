namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class PurchaseAuthorizationService
{
    public void EnsureCanChargeAccount(
        Customer customer,
        CustomerAccountSnapshot account,
        Order order,
        decimal cafeteriaAccountAmount)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(order);

        if (order.CustomerId != customer.Id)
            throw new InvalidOperationException("The order does not belong to the supplied customer.");
        if (order.Items.Count == 0)
            throw new InvalidOperationException("An empty order cannot be authorized.");
        if (cafeteriaAccountAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(cafeteriaAccountAmount));
        if (cafeteriaAccountAmount > order.Total)
            throw new InvalidOperationException("The cafeteria-account amount cannot exceed the order total.");
        if (cafeteriaAccountAmount > account.AvailableCredit)
            throw new InvalidOperationException("The cafeteria-account portion exceeds the customer's available cafeteria credit.");
    }

    [Obsolete("Use EnsureCanChargeAccount and pass the cafeteria-account portion of the order.")]
    public void EnsureCanPurchase(Customer customer, CustomerAccountSnapshot account, Order order)
        => EnsureCanChargeAccount(customer, account, order, order.Total);
}
