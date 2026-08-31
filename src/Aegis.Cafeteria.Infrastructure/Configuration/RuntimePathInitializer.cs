namespace Aegis.Cafeteria.Infrastructure.Configuration;

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
        const string prefix = "Data Source=";
        var part = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => x.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (part is null) return;
        var path = part[(part.IndexOf('=') + 1)..].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || path == ":memory:") return;
        EnsureParent(path);
    }

    private static void EnsureParent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }
}
