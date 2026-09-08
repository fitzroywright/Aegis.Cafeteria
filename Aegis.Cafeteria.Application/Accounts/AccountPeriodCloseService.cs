namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class AccountPeriodCloseService(IAccountPeriodCloseAuthorizer authorizer)
{
    public AccountPeriodClose Close(
        Customer customer,
        AccountPeriod period,
        CustomerAccountSnapshot snapshot,
        string closedBy,
        DateTimeOffset closedAt)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(closedBy)) throw new ArgumentException("Closing user is required.", nameof(closedBy));
        if (!authorizer.CanClose(closedBy)) throw new UnauthorizedAccessException("The user is not authorized to close account periods.");
        if (closedAt < new DateTimeOffset(period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero))
            throw new InvalidOperationException("An account period cannot be closed before the period has ended.");

        return new AccountPeriodClose(Guid.NewGuid(), customer.Id, period, snapshot.EmployeeAmountDue, closedBy, closedAt);
    }
}
