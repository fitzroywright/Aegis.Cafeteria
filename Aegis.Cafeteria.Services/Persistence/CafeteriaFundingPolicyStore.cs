namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Application.Integration;

using Microsoft.EntityFrameworkCore;

public sealed class CafeteriaFundingPolicyStore : ICafeteriaFundingPolicySource
{
    private const int CurrentPolicyId = 1;

    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly AuditTrailService auditTrail;

    public CafeteriaFundingPolicyStore
    (
        IDbContextFactory<ServiceDbContext> factory
    )
    {
        this.factory = factory;
        this.auditTrail = new AuditTrailService(factory);
    }

    public async Task<CafeteriaFundingPolicySnapshot> GetFundingPolicyAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        CafeteriaFundingPolicyRow? row = await database.FundingPolicies.SingleOrDefaultAsync
        (
              policy => policy.Id == CurrentPolicyId
            , cancellationToken
        );

        if (row is null)
        {
            throw new InvalidOperationException
            (
                "Cafeteria funding policy has not been configured. Set Daily Credit Limit Rate and Daily Company Subsidy Rate before month opening."
            );
        }

        return ToSnapshot(row);
    }

    public async Task<CafeteriaFundingPolicySnapshot> SetFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , string updatedBy
        , CancellationToken cancellationToken = default
    )
    {
        if (dailyCreditLimitRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyCreditLimitRate));
        }

        if (dailyCompanySubsidyRate <= 0m || dailyCompanySubsidyRate > dailyCreditLimitRate)
        {
            throw new ArgumentOutOfRangeException
            (
                  nameof(dailyCompanySubsidyRate)
                , "Daily Company Subsidy Rate must be positive and cannot exceed the Daily Credit Limit Rate."
            );
        }

        if (string.IsNullOrWhiteSpace(updatedBy))
        {
            throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));
        }

        DateTimeOffset updatedAt = DateTimeOffset.Now;

        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        CafeteriaFundingPolicyRow? row = await database.FundingPolicies.SingleOrDefaultAsync
        (
              policy => policy.Id == CurrentPolicyId
            , cancellationToken
        );

        object? before = row is null
            ? null
            : new
            {
                  row.DailyCreditLimitRate
                , row.DailyCompanySubsidyRate
                , row.UpdatedBy
                , row.UpdatedAt
            };

        if (row is null)
        {
            row = new CafeteriaFundingPolicyRow
            {
                  Id = CurrentPolicyId
                , DailyCreditLimitRate = dailyCreditLimitRate
                , DailyCompanySubsidyRate = dailyCompanySubsidyRate
                , UpdatedBy = updatedBy.Trim()
                , UpdatedAt = updatedAt
            };

            database.FundingPolicies.Add(row);
        }
        else
        {
            row.DailyCreditLimitRate = dailyCreditLimitRate;
            row.DailyCompanySubsidyRate = dailyCompanySubsidyRate;
            row.UpdatedBy = updatedBy.Trim();
            row.UpdatedAt = updatedAt;
        }

        database.FundingPolicyHistory.Add
        (
            new CafeteriaFundingPolicyHistoryRow
            {
                  DailyCreditLimitRate = dailyCreditLimitRate
                , DailyCompanySubsidyRate = dailyCompanySubsidyRate
                , UpdatedBy = updatedBy.Trim()
                , UpdatedAt = updatedAt
            }
        );

        await database.SaveChangesAsync(cancellationToken);

        try
        {
            await this.auditTrail.RecordAsync
            (
                  row.UpdatedBy
                , "SetFundingPolicy"
                , "CafeteriaFundingPolicy"
                , CurrentPolicyId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                , before: before
                , after: new
                {
                      row.DailyCreditLimitRate
                    , row.DailyCompanySubsidyRate
                    , row.UpdatedBy
                    , row.UpdatedAt
                }
                , cancellationToken: cancellationToken
            );
        }
        catch
        {
        }

        return ToSnapshot(row);
    }

    private static CafeteriaFundingPolicySnapshot ToSnapshot
    (
        CafeteriaFundingPolicyRow row
    )
    {
        return new CafeteriaFundingPolicySnapshot
        (
              row.DailyCreditLimitRate
            , row.DailyCompanySubsidyRate
            , row.UpdatedBy
            , row.UpdatedAt
        );
    }
}
