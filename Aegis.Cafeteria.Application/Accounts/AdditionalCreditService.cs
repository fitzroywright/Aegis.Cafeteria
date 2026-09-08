namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class AdditionalCreditService(IAdditionalCreditAuthorizer authorizer)
{
    public AdditionalCreditApproval Approve(
        Customer customer,
        AccountPeriod period,
        IEnumerable<AdditionalCreditApproval> existingApprovals,
        decimal amount,
        string approvedBy,
        DateTimeOffset approvedAt,
        string? reason = null,
        Guid? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(existingApprovals);

        if (!authorizer.CanApprove(approvedBy))
            throw new UnauthorizedAccessException("The user is not authorized to approve additional credit.");
        if (!period.Contains(approvedAt))
            throw new ArgumentException("Approval must belong to the supplied account period.", nameof(approvedAt));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var alreadyApproved = existingApprovals
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ApprovedAt))
            .Sum(x => x.Amount);

        if (alreadyApproved + amount > customer.AdditionalCreditLimit)
            throw new InvalidOperationException("The approval would exceed the customer's additional credit limit.");

        return new AdditionalCreditApproval(
            operationId ?? Guid.NewGuid(),
            customer.Id,
            amount,
            approvedBy,
            approvedAt,
            reason);
    }
}
