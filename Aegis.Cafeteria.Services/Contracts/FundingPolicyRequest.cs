namespace Aegis.Cafeteria.Services;

public sealed record FundingPolicyRequest(
    decimal DailyCreditLimitRate,
    decimal DailyCompanySubsidyRate);
