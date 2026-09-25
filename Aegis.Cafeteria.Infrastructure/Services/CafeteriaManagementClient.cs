namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;

using System.Net.Http.Json;

public sealed record ManagerReportSettingsSnapshot
(
      bool WeeklySummaryEnabled
    , string? EmailAddress
    , DayOfWeek WeeklySummaryDay
    , int WeeklySummaryHourLocal
);

public sealed record PayrollDeductionDetailSnapshot
(
      int EmployeeId
    , string EmployeeCode
    , string EmployeeName
    , string Department
    , decimal MonthlyCreditLimit
    , decimal SubsidyAllocation
    , decimal CafeteriaPurchases
    , decimal CafeteriaRefunds
    , decimal StipendCardReduction
    , decimal CompanyResponsibility
    , decimal EmployeeResponsibility
    , decimal CustomerPayments
    , decimal EmployeeAmountDue
);

public sealed record PayrollDeductionReportSnapshot
(
      int Year
    , int Month
    , PayrollDeductionDetailSnapshot[] Details
    , decimal TotalEmployeeAmountDue
)
{
    public int EmployeesWithAmountDue => Details.Length;
    public async Task<PayrollDeductionReportSnapshot> GetPayrollReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        return await this.http.GetFromJsonAsync<PayrollDeductionReportSnapshot>(
            $"api/management/payroll-report?year={year}&month={month}",
            cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no payroll report.");
    }

    public async Task SendPayrollReportAsync(
        int year,
        int month,
        bool force = true,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.PostAsJsonAsync(
            $"api/management/payroll-report/send?year={year}&month={month}&force={force.ToString().ToLowerInvariant()}",
            new { },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AuditTrailSnapshot>> GetAuditTrailAsync(
        string? actor = null,
        string? action = null,
        int? customerId = null,
        bool? succeeded = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        List<string> query = [$"take={Math.Clamp(take, 1, 500)}"];
        if (!string.IsNullOrWhiteSpace(actor)) query.Add($"actor={Uri.EscapeDataString(actor.Trim())}");
        if (!string.IsNullOrWhiteSpace(action)) query.Add($"action={Uri.EscapeDataString(action.Trim())}");
        if (customerId.HasValue) query.Add($"customerId={customerId.Value}");
        if (succeeded.HasValue) query.Add($"succeeded={succeeded.Value.ToString().ToLowerInvariant()}");

        return await this.http.GetFromJsonAsync<AuditTrailSnapshot[]>(
            "api/management/audit?" + string.Join("&", query),
            cancellationToken) ?? [];
    }

    public async Task<AuditIntegritySnapshot> GetAuditIntegrityAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.GetAsync(
            "api/management/audit/integrity",
            cancellationToken);
        AuditIntegritySnapshot? result = await response.Content.ReadFromJsonAsync<AuditIntegritySnapshot>(
            cancellationToken: cancellationToken);
        if (result is not null)
        {
            return result;
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Cafeteria Services returned no audit-integrity result.");
    }

}

public sealed record AuditTrailSnapshot
(
      long Id
    , DateTimeOffset OccurredAt
    , string Actor
    , string Action
    , string EntityType
    , string? EntityId
    , int? CustomerId
    , string Source
    , bool Succeeded
    , int? StatusCode
    , string? Detail
);

public sealed record AuditIntegritySnapshot
(
      bool IsValid
    , int VerifiedRows
    , int LegacyRows
    , long? FirstInvalidAuditId
    , string? Error
);

public sealed class CafeteriaManagementClient
{
    private readonly HttpClient http;

    public CafeteriaManagementClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<CafeteriaFundingPolicySnapshot> GetFundingPolicyAsync(CancellationToken cancellationToken = default)
    {
        return await this.http.GetFromJsonAsync<CafeteriaFundingPolicySnapshot>("api/management/funding-policy", cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no funding policy.");
    }

    public async Task<CafeteriaFundingPolicySnapshot> SetFundingPolicyAsync(decimal dailyCreditLimitRate, decimal dailyCompanySubsidyRate, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.PutAsJsonAsync
        (
              "api/management/funding-policy"
            , new { DailyCreditLimitRate = dailyCreditLimitRate, DailyCompanySubsidyRate = dailyCompanySubsidyRate }
            , cancellationToken
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CafeteriaFundingPolicySnapshot>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no funding policy.");
    }

    public async Task<ManagerReportSettingsSnapshot> GetReportSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await this.http.GetFromJsonAsync<ManagerReportSettingsSnapshot>("api/management/report-settings", cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no report settings.");
    }

    public async Task<ManagerReportSettingsSnapshot> SetReportSettingsAsync(bool enabled, string? emailAddress, DayOfWeek day, int hourLocal, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.PutAsJsonAsync
        (
              "api/management/report-settings"
            , new { WeeklySummaryEnabled = enabled, EmailAddress = emailAddress, WeeklySummaryDay = day, WeeklySummaryHourLocal = hourLocal }
            , cancellationToken
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ManagerReportSettingsSnapshot>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no report settings.");
    }

    public async Task SendTestEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.PostAsJsonAsync
        (
              "api/management/report-settings/test-email"
            , new { EmailAddress = emailAddress }
            , cancellationToken
        );
        response.EnsureSuccessStatusCode();
    }
}
