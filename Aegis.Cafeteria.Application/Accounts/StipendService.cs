namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class StipendService(IStipendAuthorizer authorizer)
{
    public Stipend Process(Customer customer, CustomerAccountSnapshot account, decimal amount,
        string processedByUserId, DateTimeOffset processedAt, string? reason = null, Guid? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);
        if (!authorizer.CanProcessStipend(processedByUserId))
            throw new UnauthorizedAccessException("The user is not authorized to deduct cafeteria stipends.");
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > account.AvailableCredit)
            throw new InvalidOperationException("Insufficient available cafeteria credit for the requested stipend deduction.");
        return new Stipend(operationId ?? Guid.NewGuid(), customer.Id, amount, processedByUserId, processedAt, reason);
    }
}
