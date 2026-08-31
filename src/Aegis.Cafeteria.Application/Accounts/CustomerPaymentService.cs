namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class CustomerPaymentService
{
    public CustomerPayment RecordPayment(
        Customer customer,
        CustomerAccountSnapshot account,
        decimal amount,
        DateTimeOffset receivedAt,
        string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);

        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > account.EmployeeAmountDue)
            throw new InvalidOperationException(
                "Payment cannot exceed the amount currently due from the customer.");

        return new CustomerPayment(
            Guid.NewGuid(),
            customer.Id,
            amount,
            receivedAt,
            reference);
    }
}
