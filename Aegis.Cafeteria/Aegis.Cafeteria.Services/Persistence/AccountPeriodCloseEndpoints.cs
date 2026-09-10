namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

public static class AccountPeriodCloseEndpoints
{
    public static void Map(WebApplication application)
    {
        application.MapGet
        (
              "/api/management/account-periods/{year:int}/{month:int}/close-status"
            , async
            (
                  int year
                , int month
                , IDbContextFactory<ServiceDbContext> factory
                , ServiceEventStore events
                , CancellationToken cancellationToken
            ) =>
            {
                if (year < 2000 || year > 9999 || month is < 1 or > 12)
                {
                    return Results.BadRequest("A valid year and month are required.");
                }

                IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations = await events.GetCurrentMonthlyAllocationsAsync(year, month, cancellationToken);
                AccountPeriodCloseServiceEventQuery query = new(factory);
                IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> closes = await query.GetMonthAsync(year, month, cancellationToken);
                AccountPeriodCloseServiceEvent[] allocatedCloses = allocations.Keys
                    .Where(closes.ContainsKey)
                    .Select(customerId => closes[customerId])
                    .ToArray();

                return Results.Ok
                (
                    new
                    {
                          Year = year
                        , Month = month
                        , AllocatedEmployees = allocations.Count
                        , ClosedEmployees = allocatedCloses.Length
                        , IsClosed = allocations.Count > 0 && allocations.Keys.All(closes.ContainsKey)
                        , FinalEmployeeAmountDue = allocatedCloses.Sum(close => close.EmployeeAmountDue)
                        , LastClosedAt = allocatedCloses.Length == 0 ? (DateTimeOffset?)null : allocatedCloses.Max(close => close.ClosedAt)
                    }
                );
            }
        ).RequireAuthorization("CafeteriaManagement");

        application.MapPost
        (
              "/api/management/account-periods/{year:int}/{month:int}/close"
            , async
            (
                  int year
                , int month
                , HttpContext http
                , ServiceEventStore events
                , StipendAccountSnapshotFactory snapshots
                , IWorkforceReferenceSource workforce
                , IDbContextFactory<ServiceDbContext> factory
                , ManagementApiOptions managementOptions
                , CancellationToken cancellationToken
            ) =>
            {
                if (year < 2000 || year > 9999 || month is < 1 or > 12)
                {
                    return Results.BadRequest("A valid year and month are required.");
                }

                DateOnly selectedPeriod = new(year, month, 1);
                DateOnly currentPeriod = new(DateTime.Today.Year, DateTime.Today.Month, 1);
                if (selectedPeriod >= currentPeriod)
                {
                    return Results.Conflict(new { error = "Only a completed month can be closed." });
                }

                IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations = await events.GetCurrentMonthlyAllocationsAsync(year, month, cancellationToken);
                if (allocations.Count == 0)
                {
                    return Results.Conflict(new { error = $"Month {year:D4}-{month:D2} has not been opened." });
                }

                AccountPeriodCloseServiceEventQuery closeQuery = new(factory);
                IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> existingCloses = await closeQuery.GetMonthAsync(year, month, cancellationToken);
                IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(cancellationToken);
                Dictionary<int, ExternalCustomerSnapshot> employeeMap = employees.ToDictionary(employee => employee.CustomerId);

                string actor = RequireActor(http);
                IHttpContextAccessor accessor = new HttpContextAccessor { HttpContext = http };
                AccountPeriodCloseService closeService = new(new AuthenticatedAccountPeriodCloseAuthorizer(accessor, managementOptions));
                DateTimeOffset closedAt = DateTimeOffset.Now;
                int created = 0;

                foreach (MonthlyAllocationServiceEvent allocation in allocations.Values.OrderBy(allocation => allocation.CustomerId))
                {
                    if (existingCloses.ContainsKey(allocation.CustomerId))
                    {
                        continue;
                    }

                    CustomerAccountSnapshot snapshot = await snapshots.BuildAsync(allocation.CustomerId, year, month, cancellationToken);
                    ExternalCustomerSnapshot? employee = employeeMap.GetValueOrDefault(allocation.CustomerId);
                    Customer customer = new
                    (
                          allocation.CustomerId
                        , employee?.Name ?? allocation.EmployeeName
                        , employee?.Photo
                        , allocation.IsEligibleForCredit
                        , employee?.AdditionalCreditLimit ?? 0m
                    );
                    AccountPeriod period = new(year, month, allocation.EligibleWorkingDays);
                    AccountPeriodClose close = closeService.Close(customer, period, snapshot, actor, closedAt);
                    AccountPeriodCloseServiceEvent payload = new
                    (
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
                        , close.ClosedAt
                    );

                    await events.AppendAsync
                    (
                          close.Id
                        , CafeteriaServiceEventKind.AccountPeriodClose
                        , close.CustomerId
                        , close.ClosedAt
                        , payload
                        , cancellationToken
                    );
                    created++;
                }

                IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> finalCloses = await closeQuery.GetMonthAsync(year, month, cancellationToken);
                AccountPeriodCloseServiceEvent[] allocatedCloses = allocations.Keys
                    .Where(finalCloses.ContainsKey)
                    .Select(customerId => finalCloses[customerId])
                    .ToArray();

                return Results.Ok
                (
                    new
                    {
                          Year = year
                        , Month = month
                        , AllocatedEmployees = allocations.Count
                        , ClosedEmployees = allocatedCloses.Length
                        , NewlyClosedEmployees = created
                        , IsClosed = allocations.Keys.All(finalCloses.ContainsKey)
                        , FinalEmployeeAmountDue = allocatedCloses.Sum(close => close.EmployeeAmountDue)
                        , ClosedBy = actor
                        , ClosedAt = closedAt
                    }
                );
            }
        ).RequireAuthorization("CafeteriaManagement");
    }

    private static string RequireActor(HttpContext http)
    {
        string? name = http.User.Identity?.Name;
        if (http.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("Authenticated management identity is required.");
        }

        return name.Trim();
    }
}
