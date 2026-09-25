namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

public sealed class FfpFundingPolicyCompatibilityGateway(
    FfpIntegrationOptions options,
    FfpSqlConnectionFactory connections) : IFundingPolicyCompatibilityGateway
{
    public async Task MirrorFundingPolicyAsync(
        decimal dailyCreditLimitRate,
        decimal dailyCompanySubsidyRate,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
UPDATE dbo.LunchItems
   SET Price = @Price
 WHERE Code = @CreditLimitCode;

IF @@ROWCOUNT <> 1
    THROW 51000, 'Configured credit-limit compatibility policy row was not found or was not unique.', 1;

UPDATE dbo.LunchItems
   SET Cost = @Cost
 WHERE Code = @SubsidyCode;

IF @@ROWCOUNT <> 1
    THROW 51000, 'Configured subsidy compatibility policy row was not found or was not unique.', 1;";

        await using SqlConnection connection = await connections.OpenWriteAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@Price", dailyCreditLimitRate);
        command.Parameters.AddWithValue("@Cost", dailyCompanySubsidyRate);
        command.Parameters.AddWithValue("@CreditLimitCode", options.CreditLimitLunchItemCode);
        command.Parameters.AddWithValue("@SubsidyCode", options.SubsidyLunchItemCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
