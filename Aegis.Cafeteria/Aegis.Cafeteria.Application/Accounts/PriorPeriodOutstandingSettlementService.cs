namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class PriorPeriodOutstandingSettlementService(
    IPriorPeriodSettlementAuthorizer authorizer)
{
    public PriorPeriodOutstandingSettlement Approve(
        Customer customer,
        AccountPeriod settlementPeriod,
        int sourceYear,
        int sourceMonth,
        decimal amount,
        string approvedBy,
        DateTimeOffset approvedAt,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(settlementPeriod);

        if (!authorizer.CanApprove(approvedBy))
            throw new UnauthorizedAccessException(
                "The user is not authorized to approve prior-period outstanding settlements.");
        if (!settlementPeriod.Contains(approvedAt))
            throw new ArgumentException(
                "Approval must belong to the supplied settlement period.", nameof(approvedAt));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "A reason is required for a prior-period settlement.", nameof(reason));

        return new PriorPeriodOutstandingSettlement(
            Guid.NewGuid(),
            customer.Id,
            sourceYear,
            sourceMonth,
            settlementPeriod.Year,
            settlementPeriod.Month,
            amount,
            approvedBy,
            approvedAt,
            reason);
    }
}
