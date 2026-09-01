namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

public sealed class FfpLunchItemBootstrapSource(FfpIntegrationOptions options) : ILunchItemBootstrapSource
{
    public async Task<IReadOnlyList<ExternalProductSnapshot>> GetLunchItemsAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("FFP integration must be enabled and configured before LunchItems can be imported.");
        }

        const string sql = @"
SELECT
      ID
    , ISNULL(CONVERT(nvarchar(64), Code), '') AS Code
    , ISNULL(CONVERT(nvarchar(256), Name), '') AS Name
    , ISNULL(Price, 0) AS Price
    , ISNULL(Cost, 0) AS Cost
FROM dbo.LunchItems
ORDER BY ID;";

        List<ExternalProductSnapshot> result = [];
        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add
            (
                new ExternalProductSnapshot
                (
                      Convert.ToInt32(reader.GetValue(0))
                    , Convert.ToString(reader.GetValue(1))?.Trim() ?? string.Empty
                    , Convert.ToString(reader.GetValue(2))?.Trim() ?? string.Empty
                    , Convert.ToDecimal(reader.GetValue(3))
                    , Convert.ToDecimal(reader.GetValue(4))
                )
            );
        }

        return result;
    }
}
