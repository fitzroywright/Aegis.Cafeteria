namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Creates immutable monthly funding once per month.
/// Credit limit = eligible working days x configured credit-limit LunchItem.Price (default FFP00).
/// Subsidy = eligible working days x configured subsidy LunchItem.Cost (default FFP00), capped at the credit limit.
/// Current-month starters are prorated from their employment start date and inactive employees receive no credit.
/// </summary>
public sealed class MonthlyCreditAllocationService(
    IWorkingDayProvider workingDayProvider,
    ICafeteriaRatePolicyProvider policyProvider,
    IMonthlyCreditAllocationStore allocationStore,
    MonthlyAllocationCalculator? calculator = null,
    IMonthlyAllocationExecutionPolicy? executionPolicy = null,
    IExternalLedgerProjectionService? externalProjection = null,
    ILogger<MonthlyCreditAllocationService>? logger = null)
{
    public async Task<MonthlyCreditAllocation> EnsureAsync(Customer customer, int year, int month, CancellationToken cancellationToken = default)
    {
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch step = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(customer);

        if (!customer.IsActive || !customer.IsEligibleForCredit)
            return new MonthlyCreditAllocation(customer.Id, year, month, 0, false, 0m, 0m, DateTimeOffset.UtcNow);

        bool allowLocalExecution = executionPolicy?.AllowLocalCalculation ?? true;
        MonthlyCreditAllocation? existing = await allocationStore.GetAsync(customer.Id, year, month, cancellationToken);
        logger?.LogInformation("PERF Allocation CustomerId={CustomerId} lookup existing {ElapsedMs} ms Found={Found}", customer.Id, step.ElapsedMilliseconds, existing is not null);
        if (existing is not null)
        {
            if (allowLocalExecution && externalProjection is not null)
            {
                step.Restart();
                await externalProjection.PublishMonthlyAllocationAsync(customer, existing, cancellationToken);
                logger?.LogInformation("PERF Allocation CustomerId={CustomerId} existing external projection {ElapsedMs} ms", customer.Id, step.ElapsedMilliseconds);
            }
            logger?.LogInformation("PERF Allocation CustomerId={CustomerId} TOTAL {ElapsedMs} ms existing", customer.Id, total.ElapsedMilliseconds);
            return existing;
        }

        if (!allowLocalExecution)
            return new MonthlyCreditAllocation(customer.Id, year, month, 0, true, 0m, 0m, DateTimeOffset.UtcNow);

        step.Restart();
        int workingDays = await workingDayProvider.GetWorkingDaysAsync(
            year,
            month,
            customer.EmploymentStartDate,
            cancellationToken);
        logger?.LogInformation("PERF Allocation CustomerId={CustomerId} working days {ElapsedMs} ms Days={WorkingDays}", customer.Id, step.ElapsedMilliseconds, workingDays);

        if (workingDays <= 0)
            return new MonthlyCreditAllocation(customer.Id, year, month, 0, false, 0m, 0m, DateTimeOffset.UtcNow);

        step.Restart();
        CafeteriaRatePolicy? policy = await policyProvider.GetAsync(cancellationToken);
        logger?.LogInformation("PERF Allocation CustomerId={CustomerId} policy read {ElapsedMs} ms Found={Found}", customer.Id, step.ElapsedMilliseconds, policy is not null);
        if (policy is null)
            return new MonthlyCreditAllocation(customer.Id, year, month, workingDays, false, 0m, 0m, DateTimeOffset.UtcNow);

        MonthlyCreditAllocation allocation = (calculator ?? new MonthlyAllocationCalculator()).Calculate(
            customer.Id,
            year,
            month,
            workingDays,
            true,
            policy.CreditLimitPrice,
            policy.SubsidyCost,
            DateTimeOffset.UtcNow);

        step.Restart();
        await allocationStore.SaveAsync(allocation, cancellationToken);
        logger?.LogInformation("PERF Allocation CustomerId={CustomerId} save allocation {ElapsedMs} ms", customer.Id, step.ElapsedMilliseconds);
        if (externalProjection is not null)
        {
            step.Restart();
            await externalProjection.PublishMonthlyAllocationAsync(customer, allocation, cancellationToken);
            logger?.LogInformation("PERF Allocation CustomerId={CustomerId} new external projection {ElapsedMs} ms", customer.Id, step.ElapsedMilliseconds);
        }
        logger?.LogInformation("PERF Allocation CustomerId={CustomerId} TOTAL {ElapsedMs} ms new", customer.Id, total.ElapsedMilliseconds);
        return allocation;
    }
}
