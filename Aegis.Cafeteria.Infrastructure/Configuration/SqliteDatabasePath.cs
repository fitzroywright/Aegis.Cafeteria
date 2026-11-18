using Microsoft.Data.Sqlite;

namespace Aegis.Cafeteria.Infrastructure.Configuration;

public static class SqliteDatabasePath
{
    public static string Resolve(string connectionString, string baseDirectory)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.DataSource == ":memory:"
            || builder.Mode == SqliteOpenMode.Memory)
            return builder.ConnectionString;

        // SQLite file URIs have their own escaping and query semantics.
        if (builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return builder.ConnectionString;

        builder.DataSource = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(builder.DataSource), baseDirectory);
        return builder.ConnectionString;
    }
}
