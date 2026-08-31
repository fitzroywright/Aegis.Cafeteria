namespace Aegis.Cafeteria.Application.Integration;

public interface ICafeteriaFundingPolicySource
{
    Task<CafeteriaFundingPolicySnapshot> GetFundingPolicyAsync
    (
        CancellationToken cancellationToken = default
    );
}

public sealed record CafeteriaFundingPolicySnapshot
(
      decimal DailyCreditLimitRate
    , decimal DailyCompanySubsidyRate
    , string UpdatedBy
    , DateTimeOffset UpdatedAt
);
