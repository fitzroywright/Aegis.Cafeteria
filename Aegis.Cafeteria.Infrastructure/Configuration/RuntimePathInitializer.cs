namespace Aegis.Cafeteria.Infrastructure.Configuration;

using Microsoft.Data.Sqlite;

public static class RuntimePathInitializer
{
    public static void EnsureDirectories(CafeteriaRuntimeOptions runtime)
    {
        EnsureSqliteDirectory(runtime.Persistence.LocalSqliteConnectionString);
        EnsureParent(runtime.Logging.LocalLogPath);
        EnsureParent(runtime.Health.HeartbeatPath);
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:"
            || builder.Mode == SqliteOpenMode.Memory
            || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return;
        EnsureParent(builder.DataSource);
    }

    private static void EnsureParent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }
}
