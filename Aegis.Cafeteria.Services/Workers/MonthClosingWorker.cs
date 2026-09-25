namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Closes completed Cafeteria account periods without requiring a management operator.
/// The worker is idempotent: existing employee close events are never recreated.
/// </summary>
public sealed class MonthClosingWorker(
    MonthOpeningOptions options,
    ServiceEventStore events,
    StipendAccountSnapshotFactory snapshots,
    IWorkforceReferenceSource workforce,
    IDbContextFactory<ServiceDbContext> factory,
    ILogger<MonthClosingWorker> logger) : BackgroundService
{
    internal const string AutomaticActor = "SYSTEM:Cafeteria.MonthLifecycle";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.AutomaticCloseEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime now = DateTime.Now;
                if (now.Hour >= options.CloseHourLocal)
                {
                    DateTime previous = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    await EnsureClosedAsync(previous.Year, previous.Month, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Cafeteria automatic month-close failed; it will retry");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    internal async Task<MonthCloseResult> EnsureClosedAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        DateOnly selectedPeriod = new(year, month, 1);
        DateOnly currentPeriod = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (selectedPeriod >= currentPeriod)
        {
            throw new InvalidOperationException("Only a completed month can be closed.");
        }

        IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations =
            await events.GetCurrentMonthlyAllocationsAsync(year, month, cancellationToken);

        if (allocations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Month {year:D4}-{month:D2} has not been opened and cannot be closed automatically.");
        }

        AccountPeriodCloseServiceEventQuery closeQuery = new(factory);
        IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> existingCloses =
            await closeQuery.GetMonthAsync(year, month, cancellationToken);

        IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(cancellationToken);
        Dictionary<int, ExternalCustomerSnapshot> employeeMap =
            employees.ToDictionary(employee => employee.CustomerId);

        AccountPeriodCloseService closeService =
            new(new AutomaticAccountPeriodCloseAuthorizer());

        DateTimeOffset closedAt = DateTimeOffset.Now;
        int created = 0;

        foreach (MonthlyAllocationServiceEvent allocation in allocations.Values.OrderBy(allocation => allocation.CustomerId))
        {
            if (existingCloses.ContainsKey(allocation.CustomerId))
            {
                continue;
            }

            CustomerAccountSnapshot snapshot = await snapshots.BuildAsync(
                allocation.CustomerId,
                year,
                month,
                cancellationToken);

            ExternalCustomerSnapshot? employee = employeeMap.GetValueOrDefault(allocation.CustomerId);
            Customer customer = new(
                  allocation.CustomerId
                , employee?.Name ?? allocation.EmployeeName
                , employee?.Photo
                , allocation.IsEligibleForCredit
                , employee?.AdditionalCreditLimit ?? 0m);

            AccountPeriod period = new(year, month, allocation.EligibleWorkingDays);
            AccountPeriodClose close = closeService.Close(
                customer,
                period,
                snapshot,
                AutomaticActor,
                closedAt);

            AccountPeriodCloseServiceEvent payload = new(
                  close.Id
                , close.CustomerId
                , year
                , month
                , allocation.EligibleWorkingDays
                , close.EmployeeAmountDue
                , snapshot.AvailableCredit
                , snapshot.RemainingCompanyContribution
                , snapshot.StipendCardReduction
                , close.ClosedBy
                , close.ClosedAt);

            await events.AppendAsync(
                  close.Id
                , CafeteriaServiceEventKind.AccountPeriodClose
                , close.CustomerId
                , close.ClosedAt
                , payload
                , cancellationToken);

            created++;
        }

        IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> finalCloses =
            await closeQuery.GetMonthAsync(year, month, cancellationToken);

        AccountPeriodCloseServiceEvent[] allocatedCloses = allocations.Keys
            .Where(finalCloses.ContainsKey)
            .Select(customerId => finalCloses[customerId])
            .ToArray();

        bool isClosed = allocations.Keys.All(finalCloses.ContainsKey);

        if (created > 0 || !isClosed)
        {
            logger.LogInformation(
                "Automatic Cafeteria close for {Year}-{Month:D2}: {Created} new close events, {Closed}/{Allocated} closed",
                year,
                month,
                created,
                allocatedCloses.Length,
                allocations.Count);
        }

        return new MonthCloseResult(
              year
            , month
            , allocations.Count
            , allocatedCloses.Length
            , created
            , isClosed
            , allocatedCloses.Sum(close => close.EmployeeAmountDue)
            , closedAt);
    }

    private sealed class AutomaticAccountPeriodCloseAuthorizer : IAccountPeriodCloseAuthorizer
    {
        public bool CanClose(string userId)
            => string.Equals(userId, AutomaticActor, StringComparison.Ordinal);
    }
}

public sealed record MonthCloseResult(
      int Year
    , int Month
    , int AllocatedEmployees
    , int ClosedEmployees
    , int NewlyClosedEmployees
    , bool IsClosed
    , decimal FinalEmployeeAmountDue
    , DateTimeOffset ClosedAt);
