namespace Aegis.Cafeteria.Services.Backup;

using Microsoft.Data.Sqlite;

public sealed record ServiceDatabaseBackupInfo
(
      string FileName
    , long SizeBytes
    , DateTimeOffset CreatedAt
);

public sealed record ServiceDatabaseRestoreRequest
(
    string FileName
);

public sealed class ServiceDatabaseBackupService
{
    private const string PendingRestoreFileName = "restore.pending";

    private readonly string connectionString;
    private readonly ServiceBackupOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ServiceDatabaseBackupService
    (
          string connectionString
        , ServiceBackupOptions options
    )
    {
        this.connectionString = connectionString;
        this.options = options;
    }

    public async Task<ServiceDatabaseBackupInfo> CreateBackupAsync
    (
          string reason
        , CancellationToken cancellationToken = default
    )
    {
        await this.gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(this.options.Directory);

            DateTimeOffset now = DateTimeOffset.Now;
            string safeReason = string.Concat
            (
                reason.Where(character => char.IsLetterOrDigit(character) || character == '-')
            );
            string fileName = $"cafeteria-services-{now:yyyyMMdd-HHmmss}-{safeReason}.db";
            string targetPath = Path.Combine(this.options.Directory, fileName);

            await using SqliteConnection source = new(this.connectionString);
            await source.OpenAsync(cancellationToken);

            SqliteConnectionStringBuilder destinationBuilder = new()
            {
                DataSource = targetPath
            };

            await using SqliteConnection destination = new(destinationBuilder.ConnectionString);
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);

            await ValidateDatabaseAsync(targetPath, cancellationToken);
            PruneExpiredBackups();

            FileInfo file = new(targetPath);
            return new ServiceDatabaseBackupInfo(file.Name, file.Length, file.CreationTimeUtc);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public IReadOnlyList<ServiceDatabaseBackupInfo> GetBackups()
    {
        Directory.CreateDirectory(this.options.Directory);

        return Directory.EnumerateFiles(this.options.Directory, "cafeteria-services-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Select(file => new ServiceDatabaseBackupInfo(file.Name, file.Length, file.CreationTimeUtc))
            .ToArray();
    }

    public async Task<string> StageRestoreAsync
    (
          string fileName
        , string requestedBy
        , CancellationToken cancellationToken = default
    )
    {
        string backupPath = ResolveBackupPath(fileName);
        await ValidateDatabaseAsync(backupPath, cancellationToken);

        ServiceDatabaseBackupInfo safetyBackup = await CreateBackupAsync
        (
              "pre-restore"
            , cancellationToken
        );

        string pendingPath = Path.Combine(this.options.Directory, PendingRestoreFileName);
        string pending = string.Join
        (
              Environment.NewLine
            , fileName
            , requestedBy
            , DateTimeOffset.UtcNow.ToString("O")
            , safetyBackup.FileName
        );

        await File.WriteAllTextAsync(pendingPath, pending, cancellationToken);
        return safetyBackup.FileName;
    }

    public static void ApplyPendingRestore
    (
          string connectionString
        , ServiceBackupOptions options
    )
    {
        string pendingPath = Path.Combine(options.Directory, PendingRestoreFileName);
        if (!File.Exists(pendingPath))
        {
            return;
        }

        string[] lines = File.ReadAllLines(pendingPath);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            throw new InvalidOperationException("The pending Cafeteria Services restore marker is invalid.");
        }

        string fileName = Path.GetFileName(lines[0]);
        if (!string.Equals(fileName, lines[0], StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The pending Cafeteria Services restore file name is invalid.");
        }

        string backupPath = Path.Combine(options.Directory, fileName);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The pending Cafeteria Services restore backup was not found.", backupPath);
        }

        SqliteConnectionStringBuilder builder = new(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
        {
            throw new InvalidOperationException("Cafeteria Services restore requires a file-backed SQLite database.");
        }

        string databasePath = Path.GetFullPath(builder.DataSource);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");

        string temporaryPath = $"{databasePath}.restore";
        File.Copy(backupPath, temporaryPath, true);
        File.Move(temporaryPath, databasePath, true);

        DeleteIfExists($"{databasePath}-wal");
        DeleteIfExists($"{databasePath}-shm");
        File.Delete(pendingPath);
    }

    private string ResolveBackupPath
    (
        string fileName
    )
    {
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("A valid backup file name is required.", nameof(fileName));
        }

        string path = Path.Combine(this.options.Directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The requested Cafeteria Services backup was not found.", fileName);
        }

        return path;
    }

    private static async Task ValidateDatabaseAsync
    (
          string path
        , CancellationToken cancellationToken
    )
    {
        SqliteConnectionStringBuilder builder = new()
        {
              DataSource = path
            , Mode = SqliteOpenMode.ReadOnly
        };

        await using SqliteConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        object? result = await command.ExecuteScalarAsync(cancellationToken);

        if (!string.Equals(Convert.ToString(result), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite integrity check failed for backup '{Path.GetFileName(path)}'.");
        }
    }

    private void PruneExpiredBackups()
    {
        DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-Math.Max(1, this.options.RetentionDays));

        foreach (FileInfo file in Directory.EnumerateFiles(this.options.Directory, "cafeteria-services-*.db").Select(path => new FileInfo(path)))
        {
            if (file.CreationTimeUtc < cutoff.UtcDateTime)
            {
                file.Delete();
            }
        }
    }

    private static void DeleteIfExists
    (
        string path
    )
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
