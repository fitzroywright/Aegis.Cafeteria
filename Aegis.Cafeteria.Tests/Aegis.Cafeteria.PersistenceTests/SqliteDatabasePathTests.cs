using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace Aegis.Cafeteria.PersistenceTests;

public sealed class SqliteDatabasePathTests
{
    [Theory]
    [InlineData("Data Source=cache/local.db;Cache=Shared")]
    [InlineData("Filename=cache/local.db;Cache=Shared")]
    public void Relative_paths_use_the_explicit_base_and_preserve_options(string connectionString)
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-path-" + Guid.NewGuid().ToString("N"));
        var resolved = new SqliteConnectionStringBuilder(SqliteDatabasePath.Resolve(connectionString, root));
        Assert.Equal(Path.Combine(root, "cache", "local.db"), resolved.DataSource);
        Assert.Equal(SqliteCacheMode.Shared, resolved.Cache);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=file:cache.db?mode=ro")]
    [InlineData("Data Source=named-cache;Mode=Memory;Cache=Shared")]
    public void Memory_databases_and_file_uris_retain_SQLite_semantics(string connectionString)
    {
        var expected = new SqliteConnectionStringBuilder(connectionString);
        var resolved = new SqliteConnectionStringBuilder(SqliteDatabasePath.Resolve(connectionString, AppContext.BaseDirectory));
        Assert.Equal(expected.ConnectionString, resolved.ConnectionString);
    }

    [Fact]
    public void Absolute_path_with_a_semicolon_is_preserved_without_touching_the_file()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-path-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "cache;local.db");
        string input = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ConnectionString;
        var resolved = new SqliteConnectionStringBuilder(SqliteDatabasePath.Resolve(input, AppContext.BaseDirectory));
        Assert.Equal(path, resolved.DataSource);
        Assert.Equal(SqliteOpenMode.ReadOnly, resolved.Mode);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Directory_initialization_supports_filename_alias_and_quoted_semicolons()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "cache;data", "local.db");
            var runtime = new CafeteriaRuntimeOptions(
                new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Filename=\"{path}\"" },
                new CafeteriaLoggingOptions(), new BitwardenSecretsManagerOptions(),
                new CafeteriaHealthOptions { HeartbeatPath = "" }, new CafeteriaMessagingOptions(), new CafeteriaServicesOptions());
            RuntimePathInitializer.EnsureDirectories(runtime);
            Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
