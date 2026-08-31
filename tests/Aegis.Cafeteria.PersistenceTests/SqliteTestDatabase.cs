namespace Aegis.Cafeteria.PersistenceTests;

using Microsoft.Data.Sqlite;

internal static class SqliteTestDatabase
{
    public static string CreatePath(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    }

    public static void Delete(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return;

        SqliteConnection.ClearAllPools();

        DeleteWithRetry(file);
        DeleteWithRetry(file + "-wal");
        DeleteWithRetry(file + "-shm");
        DeleteWithRetry(file + "-journal");
    }

    private static void DeleteWithRetry(string path)
    {
        const int attempts = 5;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        if (File.Exists(path))
            File.Delete(path);
    }
}
