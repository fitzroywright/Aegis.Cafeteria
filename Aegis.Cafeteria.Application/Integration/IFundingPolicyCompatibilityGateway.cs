namespace Aegis.Cafeteria.Application.Integration;

public interface IFundingPolicyCompatibilityGateway
{
    Task MirrorFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , CancellationToken cancellationToken = default
    );
}
