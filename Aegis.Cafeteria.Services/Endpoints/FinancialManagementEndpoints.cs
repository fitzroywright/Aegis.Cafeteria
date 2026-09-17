using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Workers;

namespace Aegis.Cafeteria.Services;

internal static class FinancialManagementEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/management/additional-credit", ApproveAdditionalCreditAsync).RequireAuthorization("CafeteriaManagement");
        app.MapPost("/api/management/stipends", ProcessStipendAsync).RequireAuthorization("CafeteriaManagement");
        app.MapGet("/api/management/funding-policy", async (CafeteriaFundingPolicyStore fundingPolicy, CancellationToken ct) => Results.Ok(await fundingPolicy.GetFundingPolicyAsync(ct))).RequireAuthorization("CafeteriaManagement");
        app.MapPut("/api/management/funding-policy", UpdateFundingPolicyAsync).RequireAuthorization("CafeteriaManagement");
        app.MapGet("/api/management/payroll-report", async (int year, int month, PayrollDeductionReportSource payroll, CancellationToken ct) => Results.Ok(await payroll.BuildAsync(year, month, ct))).RequireAuthorization("CafeteriaManagement");
        app.MapPost("/api/management/payroll-report/send", SendPayrollReportAsync).RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> ApproveAdditionalCreditAsync(AdditionalCreditRequest request, HttpContext http, ServiceEventStore store, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, AdditionalCreditService additionalCredit, CancellationToken ct)
    {
        if (request.CustomerId <= 0 || request.Amount <= 0m) return Results.BadRequest("Employee and a positive amount are required.");
        if (request.OperationId is Guid requestedId)
        {
            CafeteriaServiceEventEnvelope? existing = await store.GetByOperationAsync(requestedId, ct);
            if (existing is not null) return Results.Ok(existing);
        }

        ExternalCustomerSnapshot? employee = (await workforce.GetCustomersAsync(ct)).SingleOrDefault(x => x.CustomerId == request.CustomerId);
        if (employee is null) return Results.NotFound("Employee was not found in FFP.");
        if (!employee.IsEligibleForCafeteriaCredit) return Results.BadRequest("Employee is not eligible for cafeteria credit.");

        decimal? limit = await policies.GetAdditionalCreditLimitAsync(request.CustomerId, ct);
        if (limit is null) return Results.Problem("No employee-specific Additional Credit Limit has been configured for this employee.", statusCode: 409);

        DateTimeOffset at = request.ApprovedAt ?? DateTimeOffset.Now;
        string actor = CafeteriaActor.Require(http);
        Customer customer = new(employee.CustomerId, employee.Name, employee.Photo, employee.IsEligibleForCafeteriaCredit, limit.Value);
        AccountPeriod period = new(at.Year, at.Month, 0);
        var existingApprovals = await store.GetAdditionalCreditApprovalsAsync(request.CustomerId, at.Year, at.Month, ct);
        try
        {
            var approval = additionalCredit.Approve(customer, period, existingApprovals, request.Amount, actor, at, request.Reason, request.OperationId);
            AdditionalCreditServiceEvent payload = new(approval.Id, approval.CustomerId, approval.Amount, approval.ApprovedBy, approval.ApprovedAt, approval.Reason, limit.Value);
            CafeteriaServiceEventEnvelope serviceEvent = await store.AppendAsync(approval.Id, CafeteriaServiceEventKind.AdditionalCredit, approval.CustomerId, approval.ApprovedAt, payload, ct);
            return Results.Created($"/api/events/{serviceEvent.Sequence}", serviceEvent);
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException exception) { return Results.BadRequest(exception.Message); }
        catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
    }

    private static async Task<IResult> ProcessStipendAsync(StipendRequest request, HttpContext http, ServiceEventStore store, IWorkforceReferenceSource workforce, StipendAccountSnapshotFactory accountFactory, StipendService stipendService, CancellationToken ct)
    {
        if (request.CustomerId <= 0 || request.CashAmount <= 0m || request.CardReductionAmount < 0m)
            return Results.BadRequest("Employee and valid cash/card-reduction amounts are required.");

        if (request.OperationId is Guid requestedId)
        {
            CafeteriaServiceEventEnvelope? existing = await store.GetByOperationAsync(requestedId, ct);
            if (existing is not null) return Results.Ok(existing);
        }

        ExternalCustomerSnapshot? employee = (await workforce.GetCustomersAsync(ct)).SingleOrDefault(candidate => candidate.CustomerId == request.CustomerId);
        if (employee is null) return Results.NotFound("Employee was not found in FFP.");
        if (!employee.IsEligibleForCafeteriaCredit) return Results.BadRequest("Employee is not eligible for cafeteria credit.");

        string actor = CafeteriaActor.Require(http);
        DateTimeOffset processedAt = request.ProcessedAt ?? DateTimeOffset.Now;
        int fundingYear = request.FundingYear ?? processedAt.Year;
        int fundingMonth = request.FundingMonth ?? processedAt.Month;
        if (fundingYear < 2000 || fundingYear > 9999 || fundingMonth is < 1 or > 12)
            return Results.BadRequest("A valid stipend funding year and month are required.");

        DateOnly processingMonth = new(processedAt.Year, processedAt.Month, 1);
        DateOnly fundingPeriod = new(fundingYear, fundingMonth, 1);
        if (fundingPeriod > processingMonth) return Results.BadRequest("A stipend cannot be charged to a future funding period.");

        try
        {
            CustomerAccountSnapshot account = await accountFactory.BuildAsync(request.CustomerId, fundingYear, fundingMonth, ct);
            Customer customer = new(employee.CustomerId, employee.Name, employee.Photo, true, 0m);
            Stipend stipend = stipendService.Process(customer, account, request.CashAmount, request.PaymentMode, actor, processedAt, request.ExpenseReportReference, request.Notes, fundingYear, fundingMonth);
            if (request.CardReductionAmount != stipend.CardReduction)
                return Results.BadRequest($"Card reduction must be {stipend.CardReduction:N2} for the employee's selected funding-period subsidy and cafeteria balance.");

            Guid operationId = request.OperationId.GetValueOrDefault(Guid.NewGuid());
            StipendServiceEvent payload = new(operationId, request.CustomerId, stipend.CashAmount, stipend.CardReduction, stipend.PaymentMode, actor, stipend.ProcessedAt, stipend.ExpenseReportReference, stipend.Notes, stipend.FundingYear, stipend.FundingMonth);
            CafeteriaServiceEventEnvelope serviceEvent = await store.AppendAsync(operationId, CafeteriaServiceEventKind.Stipend, request.CustomerId, processedAt, payload, ct);
            return Results.Created($"/api/events/{serviceEvent.Sequence}", serviceEvent);
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
    }

    private static async Task<IResult> UpdateFundingPolicyAsync(FundingPolicyRequest request, HttpContext http, FundingPolicyManagementService fundingPolicy, CancellationToken ct)
    {
        if (request.DailyCreditLimitRate <= 0m || request.DailyCompanySubsidyRate <= 0m || request.DailyCompanySubsidyRate > request.DailyCreditLimitRate)
            return Results.BadRequest("Funding rates must be positive and the company subsidy rate cannot exceed the credit-limit rate.");
        return Results.Ok(await fundingPolicy.SetFundingPolicyAsync(request.DailyCreditLimitRate, request.DailyCompanySubsidyRate, CafeteriaActor.Require(http), ct));
    }

    private static async Task<IResult> SendPayrollReportAsync(int year, int month, bool? force, ScheduledEmailWorker worker, CancellationToken ct)
    {
        PayrollDeductionReportPackage report = await worker.SendPayrollReportAsync(year, month, force ?? false, ct);
        return Results.Ok(new { report.Year, report.Month, report.EmployeesWithAmountDue, report.TotalEmployeeAmountDue, Forced = force ?? false });
    }
}
