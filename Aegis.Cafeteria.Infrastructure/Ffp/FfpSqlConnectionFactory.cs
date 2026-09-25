namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

public sealed class FfpSqlConnectionFactory(FfpIntegrationOptions options)
{
    public async Task<SqlConnection> OpenReadAsync(CancellationToken cancellationToken)
    {
        EnsureEnabled();
        SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<SqlConnection> OpenWriteAsync(CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!options.AllowWrites)
        {
            throw new InvalidOperationException("FFP writes are disabled for this environment. The configured FFP database is read-only from Aegis.Cafeteria.Services.");
        }

        SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("FFP integration is disabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("FFP database connection string is not configured.");
        }
    }
}
