using System.Security.Claims;
using System.Text.Json;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Workers;
using Common.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aegis.Cafeteria.Services.Acceptance;

/// <summary>Calls production services and persistence implementations, never mock databases.</summary>
internal sealed class CafeteriaAcceptanceSuite(IServiceProvider services, AcceptanceManifest manifest,
    string sqlConnection, string directory)
{
    private readonly Dictionary<string, AcceptanceProofResult> results = [];
    private CafeteriaPolicySnapshot? policy;
    private IReadOnlyList<ExternalCustomerSnapshot> employees = [];
    private LocalDbContextFactory? localFactory;
    private EfCafeteriaPersistenceStore? local;
    private Order? sale;
    private decimal saleAmount;
    private Guid stipendId;
    private Guid scanId;
    private readonly DateTimeOffset at = new(manifest.Year, manifest.Month, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Actor = "Acceptance.Engineer";
    private IWorkforceReferenceSource Workforce => services.GetRequiredService<IWorkforceReferenceSource>();
    private ServiceEventStore Events => services.GetRequiredService<ServiceEventStore>();
    private IEmployeeIdScanPublisher Publisher => services.GetRequiredService<IEmployeeIdScanPublisher>();
    private IDbContextFactory<ServiceDbContext> ServiceFactory => services.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
    private ExternalPublicationWorker Publication => services.GetRequiredService<ExternalPublicationWorker>();
    private int Employee => manifest.FixtureEmployeeIds[0];
    private decimal Credit => manifest.ExpectedWorkingDays * manifest.ExpectedPrice;
    private decimal Subsidy => Math.Min(Credit, manifest.ExpectedWorkingDays * manifest.ExpectedCost);

    public async Task<IReadOnlyList<AcceptanceProofResult>> ExecuteAsync(Func<IReadOnlyList<AcceptanceProofResult>, Task> checkpoint)
    {
        async Task Check(string id, string expected, Func<Task<string>> action, params string[] prerequisites)
        {
            var definition = CafeteriaAcceptanceCatalogue.Required.Single(x => x.Id == id);
            if (definition.Level < manifest.Level)
            {
                results[id] = new(id, definition.Name, AcceptanceProofState.Unexecuted, expected,
                    "Not selected", "Requires complete Level 1 acceptance", DateTimeOffset.UtcNow);
                await checkpoint(Snapshot());
                return;
            }
            try
            {
                if (prerequisites.Any(p => !results.TryGetValue(p, out var r) || r.State != AcceptanceProofState.Passed))
                    throw new AcceptanceBlockedException("A required fixture or preceding workflow did not pass: " + string.Join(", ", prerequisites));
                string actual = await action();
                results[id] = new(id, definition.Name, AcceptanceProofState.Passed, expected, actual,
                    "All scenario assertions passed against isolated databases.", DateTimeOffset.UtcNow);
            }
            catch (Exception e)
            {
                results[id] = new(id, definition.Name,
                    e is AcceptanceBlockedException ? AcceptanceProofState.Blocked : AcceptanceProofState.Failed,
                    expected, e is AcceptanceAssertionException ? e.Message : e.GetType().Name,
                    e is AcceptanceBlockedException ? e.Message : "Scenario failed. " + e.Message,
                    DateTimeOffset.UtcNow);
            }
            Console.WriteLine($"{id}: {results[id].State}");
            await checkpoint(Snapshot());
        }

        await Check("CAF-01", $"FFP00 Price={manifest.ExpectedPrice}; Cost={manifest.ExpectedCost}", async () =>
        {
            policy = await services.GetRequiredService<ICafeteriaPolicyProvider>().GetPolicyAsync(manifest.Year, manifest.Month, true);
            Equal(manifest.ExpectedPrice, policy.DailyCreditLimitRate, "FFP00.Price");
            Equal(manifest.ExpectedCost, policy.DailyCompanySubsidyRate, "FFP00.Cost");
            Require(!string.IsNullOrWhiteSpace(policy.Version), "Policy version is absent");
            return JsonSerializer.Serialize(policy);
        });
        await Check("CAF-02", "All dedicated fixtures have identity fields, active eligibility and full-month employment", async () =>
        {
            employees = await Workforce.GetCustomersAsync();
            Equal(manifest.FixtureEmployeeIds.Length, employees.Count, "Eligible fixture count");
            Require(employees.All(x => x.IsActive && x.IsEligibleForCafeteriaCredit
                && !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Code)
                && x.Code.StartsWith(manifest.FixtureCodePrefix, StringComparison.Ordinal)
                && (x.EmploymentStartDate is null || x.EmploymentStartDate <= new DateOnly(manifest.Year, manifest.Month, 1))),
                "Fixture identity, eligibility or employment start does not match deterministic full-month baseline");
            return JsonSerializer.Serialize(employees.Select(x => new { x.CustomerId, x.Code, x.Name, x.IsActive, x.IsEligibleForCafeteriaCredit }));
        });
        await Check("CAF-03", $"Holidays={string.Join(',', manifest.ExpectedHolidays)}; WorkingDays={manifest.ExpectedWorkingDays}", async () =>
        {
            var holidays = await Workforce.GetHolidaysAsync(manifest.Year, manifest.Month);
            Equal(string.Join(',', manifest.ExpectedHolidays.Order()), string.Join(',', holidays.Select(x => x.ToString("yyyy-MM-dd")).Order()), "Holiday dates");
            Equal(manifest.ExpectedWorkingDays, policy!.WorkingDays.Count, "Policy working days");
            var calculator = services.GetRequiredService<MonthlyAllocationCalculator>();
            Equal(manifest.ExpectedWorkingDays, calculator.CountWorkingDays(manifest.Year, manifest.Month, holidays.Concat(holidays)), "Duplicate holidays");
            Equal(0, calculator.CountWorkingDays(manifest.Year, manifest.Month, holidays, new DateOnly(manifest.Year, manifest.Month, 1).AddMonths(1)), "Future employment");
            Require(policy.WorkingDays.All(x => x.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(x)), "Weekend/holiday included");
            return $"Calendar matches; {policy.WorkingDays.Count} working days; duplicate and future-start boundaries passed";
        }, "CAF-01");
        await Check("CAF-10", $"{manifest.FixtureEmployeeIds.Length} allocations; repeated opening creates 0", async () =>
        {
            var opening = services.GetRequiredService<MonthOpeningWorker>();
            Equal(employees.Count, await opening.EnsureMonthAsync(manifest.Year, manifest.Month, default), "Initial opening");
            Equal(0, await opening.EnsureMonthAsync(manifest.Year, manifest.Month, default), "Repeated opening");
            localFactory = new(new DbContextOptionsBuilder<LocalCafeteriaDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "runtime.sqlite")}").Options);
            await new PersistenceInitializer(localFactory, NullLogger<PersistenceInitializer>.Instance).InitializeLocalAsync();
            local = new(new LocalPersistenceStore(localFactory, NullLogger<LocalPersistenceStore>.Instance));
            await local.SaveProductAsync(new Product(1, "Acceptance Lunch", 1));
            foreach (var employee in employees)
            {
                await local.SaveCustomerAsync(new Customer(employee.CustomerId, employee.Name, employee.Photo, true, 100));
                var value = (await Events.GetCurrentMonthlyAllocationsAsync(manifest.Year, manifest.Month))[employee.CustomerId];
                await local.SaveMonthlyCreditAllocationAsync(new MonthlyCreditAllocation(employee.CustomerId, value.Year, value.Month,
                    value.EligibleWorkingDays, value.IsEligibleForCredit, value.CreditLimitPrice, value.SubsidyCost, value.CalculatedAt));
            }
            return "Actual month-opening worker is idempotent; service and local SQLite schemas initialized";
        }, "CAF-01", "CAF-02", "CAF-03");
        await Check("CAF-04", $"Every fixture credit limit={Credit}", async () =>
        {
            var allocations = await Events.GetCurrentMonthlyAllocationsAsync(manifest.Year, manifest.Month);
            foreach (var value in allocations.Values) Equal(Credit, value.EligibleWorkingDays * value.CreditLimitPrice, "Credit limit");
            return $"{allocations.Count} service allocations persist credit limit {Credit}";
        }, "CAF-10");
        await Check("CAF-05", $"Subsidy=min(credit, days × Cost)={Subsidy}; capped when Cost>Price", async () =>
        {
            foreach (var employee in employees) Equal(Subsidy, (await local!.GetAsync(employee.CustomerId, manifest.Year, manifest.Month))!.SubsidyAllocation, "Persisted subsidy");
            var boundary = new MonthlyCreditAllocation(Employee, manifest.Year, manifest.Month, 1, true, 100, 200, at);
            Equal(100m, boundary.SubsidyAllocation, "Subsidy cap");
            return $"Persisted subsidy={Subsidy}; cap boundary=100";
        }, "CAF-10");
        await Check("CAF-07", "One real EmployeeIDScans row with the fixture ID, identifier and amount 25", async () =>
        {
            scanId = Guid.NewGuid();
            await Publisher.PublishAsync(new(scanId, Employee, "Purchase", at, 25, "Acceptance identification proof", "NFC", Actor, employees[0].Code));
            var scan = await ReadScan(scanId);
            Equal(1, scan.Count, "Scan count"); Equal(25m, scan.Amount, "Scan amount"); Equal(8, scan.Type, "Purchase scan type");
            Equal(Employee, scan.EmployeeId, "Scan employee"); Equal(employees[0].Code!, scan.Code, "Identifier");
            return JsonSerializer.Serialize(scan);
        }, "CAF-02");
        await Check("CAF-08", "Stipend decreases available balance by 10; retry adds no row; invalid and excessive amounts rejected; outbound scan type 4", async () =>
        {
            DateTime now = DateTime.Now;
            await services.GetRequiredService<MonthOpeningWorker>().EnsureMonthAsync(now.Year, now.Month, default);
            var snapshots = services.GetRequiredService<StipendAccountSnapshotFactory>();
            var before = await snapshots.BuildAsync(Employee, now.Year, now.Month);
            Require(before.AvailableCredit >= 10, "Current-month fixture requires at least 10 available credit");
            using var scope = services.CreateScope();
            var http = FixtureHttp(scope.ServiceProvider);
            var stipend = scope.ServiceProvider.GetRequiredService<StipendService>();
            stipendId = Guid.NewGuid();
            async Task<IResult> Request(decimal amount, Guid id) => await FinancialManagementEndpoints.ProcessStipendAsync(
                new(Employee, amount, "Acceptance deduction", id), http, Events, Workforce, snapshots, stipend, default);
            Equal(400, Status(await Request(-1, Guid.NewGuid())), "Negative stipend rejection");
            Equal(409, Status(await Request(before.AvailableCredit + 1, Guid.NewGuid())), "Insufficient credit rejection");
            Equal(201, Status(await Request(10, stipendId)), "Stipend status");
            Equal(200, Status(await Request(10, stipendId)), "Repeat status");
            var after = await snapshots.BuildAsync(Employee, now.Year, now.Month);
            Equal(before.AvailableCredit - 10, after.AvailableCredit, "Deduction-only balance");
            Require(after.AvailableCredit >= 0, "Stipend created negative balance");
            await Publication.PublishBatchAsync(default);
            var scan = await ReadScan(stipendId);
            Equal(1, scan.Count, "Stipend publication count"); Equal(10m, scan.Amount, "Stipend scan value"); Equal(4, scan.Type, "StipendCardReduction scan type");
            // Also test a real POS purchase before a subsequent deduction. A service
            // that ignores incoming POS events must not qualify the balance guard.
            var allocation = (await Events.GetCurrentMonthlyAllocationsAsync(now.Year, now.Month))[Employee];
            await local!.SaveMonthlyCreditAllocationAsync(new(Employee, now.Year, now.Month, allocation.EligibleWorkingDays,
                true, allocation.CreditLimitPrice, allocation.SubsidyCost, allocation.CalculatedAt));
            await local.SaveStipendAsync(new(stipendId, Employee, 10, Actor, now, "Acceptance deduction"));
            decimal purchaseAmount = after.AvailableCredit - 1;
            Require(purchaseAmount >= 2, "Stipend spending-boundary fixture needs at least 3 remaining credit");
            var purchase = Sale(Employee, purchaseAmount, DateTimeOffset.Now);
            await local.SaveOrderAsync(purchase); await TransferPos(purchase);
            var afterPurchase = await snapshots.BuildAsync(Employee, now.Year, now.Month);
            Equal(1m, afterPurchase.AvailableCredit, "Service reconciliation after POS spending");
            Equal(409, Status(await Request(2, Guid.NewGuid())), "Post-purchase negative balance prevention");
            return $"Balance {before.AvailableCredit}→{after.AvailableCredit}; one scan with Amount=10 and ScanType=4";
        }, "CAF-10");
        await Check("CAF-09", "Additional credit adds 15 exactly once; employee limit and invalid amount enforced", async () =>
        {
            using var scope = services.CreateScope(); var http = FixtureHttp(scope.ServiceProvider);
            var policies = services.GetRequiredService<CustomerCreditPolicyStore>();
            await policies.SetAdditionalCreditLimitAsync(Employee, 100, Actor);
            var id = Guid.NewGuid(); var service = scope.ServiceProvider.GetRequiredService<AdditionalCreditService>();
            async Task<IResult> Request(decimal amount, Guid operation) => await FinancialManagementEndpoints.ApproveAdditionalCreditAsync(
                new(Employee, amount, "Acceptance credit", at, operation), http, Events, policies, Workforce, service, default);
            Equal(400, Status(await Request(0, Guid.NewGuid())), "Zero additional credit");
            Equal(400, Status(await Request(101, Guid.NewGuid())), "Employee-specific limit");
            Equal(201, Status(await Request(15, id)), "Additional credit status");
            Equal(200, Status(await Request(15, id)), "Additional credit retry");
            Equal(15m, await Events.GetAdditionalCreditTotalAsync(Employee, manifest.Year, manifest.Month), "Credit sum");
            await local!.SaveAdditionalCreditApprovalAsync(new(id, Employee, 15, Actor, at, "Acceptance credit"));
            return "15 additional credit persisted once in the Services event stream and POS ledger";
        }, "CAF-10");
        await Check("CAF-18", "Parallel repeated SQL publication and repeated service append persist one operation", async () =>
        {
            var tx = new CafeteriaOutboundTransaction(scanId, Employee, "Purchase", at, 25, "Repeat acceptance scan", "NFC", Actor);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Publisher.PublishAsync(tx)));
            Equal(1, (await ReadScan(scanId)).Count, "Concurrent FFP duplicates");
            var envelope = (await Events.GetAfterAsync(0, 500)).Single(x => x.OperationId == stipendId);
            var payload = JsonSerializer.Deserialize<StipendServiceEvent>(envelope.PayloadJson)!;
            for (int i = 0; i < 3; i++) await Events.AppendAsync(stipendId, CafeteriaServiceEventKind.Stipend, Employee, payload.ProcessedAt, payload);
            await using var db = await ServiceFactory.CreateDbContextAsync();
            Equal(1, await db.Events.CountAsync(x => x.OperationId == stipendId), "Service duplicates");
            return "4 parallel SQL retries and 3 service retries preserve one operation each";
        }, "CAF-07", "CAF-08");
        await Check("CAF-19", "A committed external scan without a local acknowledgement recovers once after worker recreation", async () =>
        {
            var id = Guid.NewGuid();
            var payload = new AdditionalCreditServiceEvent(id, Employee, 1, Actor, at, "Interrupted acknowledgement", 100);
            await Events.AppendAsync(id, CafeteriaServiceEventKind.AdditionalCredit, Employee, at, payload);
            // Real external commit followed by a deliberately omitted local success acknowledgement.
            await Publisher.PublishAsync(new(id, Employee, "AdditionalCredit", at, 1, "Interrupted acknowledgement", "Management", Actor));
            await Events.RecordPublicationAttemptAsync(id, "Acceptance injected interruption after external commit", false);
            var restarted = new ExternalPublicationWorker(Events, Publisher, new ExternalPublicationOptions(), NullLogger<ExternalPublicationWorker>.Instance);
            await restarted.PublishBatchAsync(default);
            Equal(1, (await ReadScan(id)).Count, "Recovered external scan");
            await using var db = await ServiceFactory.CreateDbContextAsync();
            var row = await db.Events.SingleAsync(x => x.OperationId == id);
            Require(row.ExternalPublishedAt.HasValue && row.PublicationLastError is null && row.PublicationAttemptCount >= 2, "Interrupted outbox was not recovered");
            return $"External count=1; local attempts={row.PublicationAttemptCount}; publication acknowledged";
        }, "CAF-10");
        await Check("CAF-14", "Real POS sale/refund/void persistence; prior-period refund credit; closed-month mutations rejected", async () =>
        {
            // Retain a nonzero employee liability after refund, so payroll and debt proofs
            // cannot pass merely because every balance is zero.
            saleAmount = Math.Min(Credit, Subsidy + 10);
            Require(saleAmount >= 2, "Financial fixture needs at least 2 credit");
            sale = Sale(Employee, saleAmount, at);
            await local!.SaveOrderAsync(sale);
            await TransferPos(sale);
            var first = sale.Items.First();
            var refundService = new RefundService(new FixtureRefundAuthorizer());
            refundService.Refund(sale, [(first.Id, 1)], first.Total, 0, Actor, at.AddMinutes(5), "Acceptance partial return");
            await local.SaveOrderAsync(sale); await TransferPos(sale);
            Equal(first.Total, sale.RefundedAmount, "Partial refund amount");
            await Publication.PublishBatchAsync(default);
            var partialScan = await ReadScan(sale.Refunds.Single().Id);
            Equal(1, partialScan.Count, "Partial refund FFP scan count");
            Equal(first.Total, partialScan.Amount, "Partial refund FFP amount must be the refund delta, not the original sale total");
            var voided = Sale(manifest.FixtureEmployeeIds[1], 2, at.AddHours(1));
            await local.SaveOrderAsync(voided); await TransferPos(voided);
            voided.Void(Actor, at.AddHours(2), "Acceptance void");
            await local.SaveOrderAsync(voided); await TransferPos(voided);
            Equal(OrderStatus.Voided, voided.Status, "Void state");
            await using var db = await localFactory!.CreateDbContextAsync();
            Equal(2, await db.Orders.CountAsync(x => x.AccountYear == manifest.Year && x.AccountMonth == manifest.Month), "Persisted historical orders");
            // Late refund uses the actual period-aware service, and persists its explicit credit.
            DateTimeOffset next = new DateTimeOffset(manifest.Year, manifest.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var close = new AccountPeriodClose(Guid.NewGuid(), Employee, new(manifest.Year, manifest.Month, manifest.ExpectedWorkingDays), 0, Actor, next);
            var remaining = sale.Items.Last();
            var late = refundService.RefundWithPeriodHandling(sale, [(remaining.Id, 1)], remaining.Total, 0, Actor, next.AddDays(2),
                "Acceptance late refund", close.Period, new(next.Year, next.Month, 0), close);
            Equal(remaining.Total, late.PriorPeriodRefundCredit!.CafeteriaAccountAmount, "Prior-period credit");
            await local.SavePriorPeriodRefundCreditAsync(late.PriorPeriodRefundCredit);
            await local.SaveAccountPeriodCloseAsync(close);
            await RejectAsync(() => local.SaveStipendAsync(new(Guid.NewGuid(), Employee, 1, Actor, at)), "Closed-period stipend");
            return "Sale, partial refund, void, prior-period credit and closed-period guard persisted in real POS SQLite";
        }, "CAF-10", "CAF-09");
        await Check("CAF-15", "Service payroll reconciles actual POS financial events; output includes remaining nonzero employee liability", async () =>
        {
            var report = await services.GetRequiredService<PayrollDeductionReportSource>().BuildAsync(manifest.Year, manifest.Month);
            // First item is 1; second item (saleAmount-1) remains charged in the service
            // stream. The late credit is in the next period, not the closed source period.
            decimal expected = Math.Max(0, saleAmount - 1 - Subsidy);
            decimal actual = report.Details.SingleOrDefault(x => x.EmployeeId == Employee)?.EmployeeAmountDue ?? 0;
            Equal(expected, actual, "POS-to-payroll liability");
            Require(PayrollDeductionReportSource.BuildDetailCsv(report).Length > 0, "Payroll CSV absent");
            throw new AcceptanceBlockedException($"Calculation and CSV checked (expected={expected}, actual={actual}); no payroll recipient-system acknowledgement contract is configured. Payroll integration remains unproven.");
        }, "CAF-14");
        await Check("CAF-11", "Close refuses pending financial publication, is idempotent and opens the following month", async () =>
        {
            var closing = services.GetRequiredService<MonthClosingWorker>();
            var pendingId = Guid.NewGuid();
            await Events.AppendAsync(pendingId, CafeteriaServiceEventKind.AdditionalCredit, Employee, at,
                new AdditionalCreditServiceEvent(pendingId, Employee, 1, Actor, at, "Close guard fixture", 100));
            await RejectAsync(async () => { await closing.EnsureClosedAsync(manifest.Year, manifest.Month, default); }, "Pending publication close guard");
            await Publication.PublishBatchAsync(default);
            var closed = await closing.EnsureClosedAsync(manifest.Year, manifest.Month, default);
            await closing.EnsureClosedAsync(manifest.Year, manifest.Month, default);
            var values = await new AccountPeriodCloseServiceEventQuery(ServiceFactory).GetMonthAsync(manifest.Year, manifest.Month);
            Equal(employees.Count, values.Count, "Close count");
            var next = new DateOnly(manifest.Year, manifest.Month, 1).AddMonths(1);
            await services.GetRequiredService<MonthOpeningWorker>().EnsureMonthAsync(next.Year, next.Month, default);
            Equal(employees.Count, (await Events.GetCurrentMonthlyAllocationsAsync(next.Year, next.Month)).Count, "Rollover allocations");
            return JsonSerializer.Serialize(closed);
        }, "CAF-10");
        await Check("CAF-12", "Following-month capacity equals its new allocation; unused prior positive credit is absent", async () =>
        {
            var next = new DateOnly(manifest.Year, manifest.Month, 1).AddMonths(1);
            int untouchedEmployee = manifest.FixtureEmployeeIds[1];
            var value = (await Events.GetCurrentMonthlyAllocationsAsync(next.Year, next.Month))[untouchedEmployee];
            var snapshot = await services.GetRequiredService<StipendAccountSnapshotFactory>().BuildAsync(untouchedEmployee, next.Year, next.Month);
            Equal(value.EligibleWorkingDays * value.CreditLimitPrice, snapshot.TotalSpendingCapacity, "Positive credit expiry");
            return JsonSerializer.Serialize(snapshot);
        }, "CAF-11");
        await Check("CAF-13", "Nonzero outstanding source-period employee debt is carried into following-month service balance", async () =>
        {
            var next = new DateOnly(manifest.Year, manifest.Month, 1).AddMonths(1);
            decimal expected = Math.Max(0, saleAmount - 1 - Subsidy);
            Require(expected > 0, "Debt fixture is zero; adjust FFP00 Cost below Price so this proof cannot pass vacuously");
            var snapshot = await services.GetRequiredService<StipendAccountSnapshotFactory>().BuildAsync(Employee, next.Year, next.Month);
            Equal(expected, snapshot.CarriedForwardDebt, "Service debt carry-forward");
            return JsonSerializer.Serialize(snapshot);
        }, "CAF-11", "CAF-14");
        await Check("CAF-16", "A dedicated employee eligibility change is observed by the real provider and service API", async () =>
        {
            var facts = services.GetRequiredService<IEmployeeFactSource>();
            var before = await facts.GetChangesAsync(null);
            var fixture = before.Employees.Single(x => x.EmployeeId == Employee);
            string journal = Path.Combine(directory, "eligibility-restoration.json");
            // Journal is durable before the first source mutation. Next execution restores
            // unfinished journals after database safety verification, before reading fixtures.
            await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new EligibilityRestoration(Employee, fixture.IsActive, fixture.IsEligible)));
            try
            {
                await using SqlConnection c = new(sqlConnection); await c.OpenAsync();
                await using SqlCommand update = new("UPDATE dbo.Employees SET EligibleForLunchTicket=0 WHERE ID=@id AND Code LIKE @prefix", c);
                update.Parameters.AddWithValue("@id", Employee);
                update.Parameters.AddWithValue("@prefix", manifest.FixtureCodePrefix + "%");
                Equal(1, await update.ExecuteNonQueryAsync(), "Dedicated employee update count");
                var change = await facts.GetChangesAsync(before.Version);
                Require(change.Employees.Any(x => x.EmployeeId == Employee && !x.IsEligible), "Provider did not expose revoked eligibility");
                Require(!(await Workforce.GetCustomersAsync()).Any(x => x.CustomerId == Employee), "Ineligible employee is still in funding source");
                using var scope = services.CreateScope(); var http = FixtureHttp(scope.ServiceProvider);
                var rejected = await FinancialManagementEndpoints.ApproveAdditionalCreditAsync(new(Employee, 1, "Revoked eligibility", at, Guid.NewGuid()),
                    http, Events, services.GetRequiredService<CustomerCreditPolicyStore>(), Workforce,
                    scope.ServiceProvider.GetRequiredService<AdditionalCreditService>(), default);
                Equal(404, Status(rejected), "Revoked employee additional-credit rejection");
            }
            finally { await AcceptanceFixtureJournal.RestoreAsync(journal, manifest, sqlConnection); }
            Require((await Workforce.GetCustomersAsync()).Any(x => x.CustomerId == Employee), "Restored fixture is absent");
            return "Real FFP eligibility change was observed and rejected for new funding; source row restored using durable journal";
        }, "CAF-02");
        await Check("CAF-17", "Every published service financial operation has exactly one matching FFP scan", async () =>
        {
            await Publication.PublishBatchAsync(default);
            var all = await Events.GetAfterAsync(0, 500);
            int verified = 0;
            foreach (var e in all.Where(e => e.Kind is CafeteriaServiceEventKind.Stipend or CafeteriaServiceEventKind.AdditionalCredit or CafeteriaServiceEventKind.MonthlyAllocation))
            {
                var scan = await ReadScan(e.OperationId);
                Equal(1, scan.Count, "Reconciliation count"); Equal(e.CustomerId, scan.EmployeeId, "Reconciliation employee");
                decimal expected = e.Kind switch
                {
                    CafeteriaServiceEventKind.Stipend => JsonSerializer.Deserialize<StipendServiceEvent>(e.PayloadJson)!.Amount,
                    CafeteriaServiceEventKind.AdditionalCredit => JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(e.PayloadJson)!.Amount,
                    _ => AllocationAmount(e.PayloadJson)
                };
                Equal(expected, scan.Amount, "Reconciled amount");
                Equal(e.Kind == CafeteriaServiceEventKind.Stipend ? 4 : e.Kind == CafeteriaServiceEventKind.MonthlyAllocation ? 3 : 9, scan.Type, "Reconciled scan type");
                verified++;
            }
            Require(verified > 0 && all.Any(e => e.Kind == CafeteriaServiceEventKind.Stipend), "Reconciliation omitted stipend or had no financial events");
            return $"{verified} service operations match FFP count, employee, amount and scan type";
        }, "CAF-08", "CAF-09");
        await Check("CAF-06", $"FFP employee CreditLimit={Credit}; AvailableBalance matches service financial projection", async () =>
        {
            await using SqlConnection connection = new(sqlConnection); await connection.OpenAsync();
            await using SqlCommand command = new("SELECT CreditLimit, AvailableBalance FROM dbo.Employees WHERE ID=@id", connection);
            command.Parameters.AddWithValue("@id", Employee);
            await using var reader = await command.ExecuteReaderAsync(); Require(await reader.ReadAsync(), "Employee projection absent");
            Equal(Credit, Convert.ToDecimal(reader.GetValue(0)), "FFP credit projection");
            var snapshot = await services.GetRequiredService<StipendAccountSnapshotFactory>().BuildAsync(Employee, manifest.Year, manifest.Month);
            Equal(snapshot.AvailableCredit, Convert.ToDecimal(reader.GetValue(1)), "FFP balance projection");
            return $"FFP credit={Credit}; balance={snapshot.AvailableCredit}";
        }, "CAF-17");
        await Check("CAF-20", "All nineteen component proofs pass, including financial reconciliation, rollover and recovery", async () =>
        {
            await Task.CompletedTask;
            var incomplete = results.Values.Where(x => x.State != AcceptanceProofState.Passed).ToArray();
            if (incomplete.Length > 0) throw new AcceptanceBlockedException("Complete workflow cannot qualify: " + string.Join(", ", incomplete.Select(x => $"{x.Id}={x.State}")));
            return "All component workflow assertions passed";
        });
        return Snapshot();
    }

    private IReadOnlyList<AcceptanceProofResult> Snapshot() => CafeteriaAcceptanceCatalogue.Required.Select(x => results.GetValueOrDefault(x.Id)
        ?? new AcceptanceProofResult(x.Id, x.Name, AcceptanceProofState.Unexecuted, "Scenario passes", "Pending", "Not executed", DateTimeOffset.UtcNow)).ToArray();
    private static decimal AllocationAmount(string json)
    { var value = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(json)!; return value.EligibleWorkingDays * value.CreditLimitPrice; }
    private static int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
    private static HttpContext FixtureHttp(IServiceProvider scoped)
    {
        var context = new DefaultHttpContext { RequestServices = scoped, User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Actor), new Claim(ClaimTypes.Role, "Cafeteria Acceptance")], "AcceptanceFixture")) };
        scoped.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }
    private static Order Sale(int employee, decimal amount, DateTimeOffset at)
    {
        var order = new Order(Guid.NewGuid(), employee, at, IDMethod.Code, "CAF-ACCEPT-SALE");
        order.AddItem(new OrderItem(1, "Acceptance item A", 1, 1));
        order.AddItem(new OrderItem(1, "Acceptance item B", amount - 1, 1));
        order.Submit(at); order.Complete([new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, amount, at)], at, Actor, "Acceptance", Actor);
        return order;
    }
    private async Task TransferPos(Order order)
    {
        var transaction = PosTransactionEventFactory.Create(order);
        await Events.AppendAsync(transaction.EventId, CafeteriaServiceEventKind.PosTransaction, transaction.CustomerId, transaction.OccurredAt, transaction);
    }
    private sealed record ScanEvidence(int Count, decimal Amount, int Type, int EmployeeId, string Code);
    private async Task<ScanEvidence> ReadScan(Guid id)
    {
        await using SqlConnection c = new(sqlConnection); await c.OpenAsync();
        await using SqlCommand cmd = new("SELECT ScanValue, ScanType, EmployeeID, Code FROM dbo.EmployeeIDScans WHERE TransmissionID=@id", c);
        cmd.Parameters.AddWithValue("@id", id.ToString("D"));
        await using var r = await cmd.ExecuteReaderAsync();
        int count = 0; decimal amount = 0; int type = 0; int employee = 0; string code = "";
        while (await r.ReadAsync()) { count++; amount = Convert.ToDecimal(r.GetValue(0)); type = Convert.ToInt32(r.GetValue(1)); employee = Convert.ToInt32(r.GetValue(2)); code = Convert.ToString(r.GetValue(3)) ?? ""; }
        return new(count, amount, type, employee, code);
    }
    private sealed class FixtureRefundAuthorizer : IRefundAuthorizer { public bool CanRefund(string userId) => userId == Actor; }
    private static void Equal<T>(T expected, T actual, string label)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new AcceptanceAssertionException($"{label}: expected={expected}; actual={actual}"); }
    private static void Require(bool condition, string message) { if (!condition) throw new AcceptanceAssertionException(message); }
    private static async Task RejectAsync(Func<Task> action, string label)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new AcceptanceAssertionException(label + ": expected rejection; actual success");
    }
}

internal sealed class AcceptanceAssertionException(string message) : Exception(message);
