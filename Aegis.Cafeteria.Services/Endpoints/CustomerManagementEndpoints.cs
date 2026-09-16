using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Services.Persistence;

namespace Aegis.Cafeteria.Services;

internal static class CustomerManagementEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/management/customers", SearchCustomersAsync).RequireAuthorization("CafeteriaManagement");
        app.MapGet("/api/management/customers/{customerId:int}/credit-policy", GetCreditPolicyAsync).RequireAuthorization("CafeteriaManagement");
        app.MapPut("/api/management/customers/{customerId:int}/credit-policy", UpdateCreditPolicyAsync).RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> SearchCustomersAsync(string? search, int? take, IWorkforceReferenceSource workforce, CancellationToken ct)
    {
        string term = (search ?? string.Empty).Trim();
        int limit = Math.Clamp(take ?? 25, 1, 100);
        if (term.Length < 2) return Results.Ok(Array.Empty<object>());

        IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(ct);
        string normalized = term.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        object[] matches = employees
            .Where(employee => employee.IsEligibleForCafeteriaCredit)
            .Where(employee => employee.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(employee.Code) && employee.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(employee.Trn) && employee.Trn.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(employee => employee.Name)
            .Take(limit)
            .Select(employee => (object)new { employee.CustomerId, employee.Name, employee.Code, employee.Trn, employee.Department })
            .ToArray();
        return Results.Ok(matches);
    }

    private static async Task<IResult> GetCreditPolicyAsync(int customerId, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, CancellationToken ct)
    {
        ExternalCustomerSnapshot? employee = (await workforce.GetCustomersAsync(ct)).SingleOrDefault(x => x.CustomerId == customerId);
        if (employee is null) return Results.NotFound("Employee was not found in FFP.");
        decimal? limit = await policies.GetAdditionalCreditLimitAsync(customerId, ct);
        return Results.Ok(new { customerId, employee.Name, AdditionalCreditLimit = limit });
    }

    private static async Task<IResult> UpdateCreditPolicyAsync(int customerId, CustomerCreditPolicyRequest request, HttpContext http, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, CancellationToken ct)
    {
        if (request.AdditionalCreditLimit < 0m) return Results.BadRequest("Additional Credit Limit cannot be negative.");
        ExternalCustomerSnapshot? employee = (await workforce.GetCustomersAsync(ct)).SingleOrDefault(x => x.CustomerId == customerId);
        if (employee is null) return Results.NotFound("Employee was not found in FFP.");
        var row = await policies.SetAdditionalCreditLimitAsync(customerId, request.AdditionalCreditLimit, CafeteriaActor.Require(http), ct);
        return Results.Ok(new { row.CustomerId, employee.Name, row.AdditionalCreditLimit, row.UpdatedBy, row.UpdatedAt });
    }
}