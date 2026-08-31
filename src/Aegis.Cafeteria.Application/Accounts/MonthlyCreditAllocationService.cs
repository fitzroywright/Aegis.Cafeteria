namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

/// <summary>
/// Creates immutable monthly funding once per month.
/// Credit limit = eligible working days x configured credit-limit LunchItem.Price (default FFP00).
/// Subsidy = eligible working days x configured subsidy LunchItem.Cost (default FFP00), capped at the credit limit.
/// </summary>
public sealed class MonthlyCreditAllocationService(
    IWorkingDayProvider workingDayProvider,
    ICafeteriaRatePolicyProvider policyProvider,
    IMonthlyCreditAllocationStore allocationStore,
    MonthlyAllocationCalculator? calculator = null,
    IMonthlyAllocationExecutionPolicy? executionPolicy = null,
    IExternalLedgerProjectionService? externalProjection = null)
{
    public async Task<MonthlyCreditAllocation> EnsureAsync(Customer customer, int year, int month, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (!customer.IsEligibleForCredit)
            return new MonthlyCreditAllocation(customer.Id, year, month, 0, false, 0m, 0m, DateTimeOffset.UtcNow);

        var allowLocalExecution = executionPolicy?.AllowLocalCalculation ?? true;
        var existing = await allocationStore.GetAsync(customer.Id, year, month, cancellationToken);
        if (existing is not null)
        {
            if (allowLocalExecution && externalProjection is not null)
                await externalProjection.PublishMonthlyAllocationAsync(customer, existing, cancellationToken);
            return existing;
        }

        if (!allowLocalExecution)
            return new MonthlyCreditAllocation(customer.Id, year, month, 0, true, 0m, 0m, DateTimeOffset.UtcNow);

        var workingDays = await workingDayProvider.GetWorkingDaysAsync(year, month, cancellationToken);
        var policy = await policyProvider.GetAsync(cancellationToken);
        if (policy is null)
            return new MonthlyCreditAllocation(customer.Id, year, month, workingDays, false, 0m, 0m, DateTimeOffset.UtcNow);

        var allocation = (calculator ?? new MonthlyAllocationCalculator()).Calculate(
            customer.Id, year, month, workingDays, true,
            policy.CreditLimitPrice, policy.SubsidyCost, DateTimeOffset.UtcNow);

        await allocationStore.SaveAsync(allocation, cancellationToken);
        if (externalProjection is not null)
            await externalProjection.PublishMonthlyAllocationAsync(customer, allocation, cancellationToken);
        return allocation;
    }
}
