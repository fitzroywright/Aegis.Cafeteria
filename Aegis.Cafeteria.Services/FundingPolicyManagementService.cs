namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

public sealed class FundingPolicyManagementService
{
    private readonly CafeteriaFundingPolicyStore fundingPolicyStore;
    private readonly IFundingPolicyCompatibilityGateway compatibilityGateway;

    public FundingPolicyManagementService
    (
          CafeteriaFundingPolicyStore fundingPolicyStore
        , IFundingPolicyCompatibilityGateway compatibilityGateway
    )
    {
        this.fundingPolicyStore = fundingPolicyStore;
        this.compatibilityGateway = compatibilityGateway;
    }

    public async Task<CafeteriaFundingPolicySnapshot> SetFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , string updatedBy
        , CancellationToken cancellationToken = default
    )
    {
        CafeteriaFundingPolicySnapshot policy = await this.fundingPolicyStore.SetFundingPolicyAsync
        (
              dailyCreditLimitRate
            , dailyCompanySubsidyRate
            , updatedBy
            , cancellationToken
        );

        await this.compatibilityGateway.MirrorFundingPolicyAsync
        (
              dailyCreditLimitRate
            , dailyCompanySubsidyRate
            , cancellationToken
        );

        return policy;
    }
}
