namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;

using System.Net.Http.Json;

public sealed class CafeteriaManagementClient
{
    private readonly HttpClient http;

    public CafeteriaManagementClient
    (
        HttpClient http
    )
    {
        this.http = http;
    }

    public async Task<CafeteriaFundingPolicySnapshot> GetFundingPolicyAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        return await this.http.GetFromJsonAsync<CafeteriaFundingPolicySnapshot>
        (
              "api/management/funding-policy"
            , cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no funding policy.");
    }

    public async Task<CafeteriaFundingPolicySnapshot> SetFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , CancellationToken cancellationToken = default
    )
    {
        using HttpResponseMessage response = await this.http.PutAsJsonAsync
        (
              "api/management/funding-policy"
            , new
            {
                  DailyCreditLimitRate = dailyCreditLimitRate
                , DailyCompanySubsidyRate = dailyCompanySubsidyRate
            }
            , cancellationToken
        );

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CafeteriaFundingPolicySnapshot>
        (
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no funding policy.");
    }


}
