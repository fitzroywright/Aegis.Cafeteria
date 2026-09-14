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
