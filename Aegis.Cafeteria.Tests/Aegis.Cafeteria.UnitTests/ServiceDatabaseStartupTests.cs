using System.Reflection;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Cafeteria.UnitTests;

public sealed class ServiceDatabaseStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_resolution_and_repeated_initialization_preserve_existing_data(bool absolute)
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-service-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string expectedPath = Path.Combine(root, "storage", "services.db");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServicePersistence:ConnectionString"] = new SqliteConnectionStringBuilder
                {
                    DataSource = absolute ? expectedPath : "storage/services.db",
                    Pooling = false
                }.ConnectionString
            }).Build();
            Type database = typeof(MonthOpeningWorker).Assembly.GetType("Aegis.Cafeteria.Services.CafeteriaServiceDatabase")!;
            string resolved = (string)database.GetMethod("ResolveConnectionString", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [configuration, root])!;
            Assert.Equal(expectedPath, new SqliteConnectionStringBuilder(resolved).DataSource);
            Assert.False(File.Exists(expectedPath));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Acceptance" });
            builder.Services.AddDbContextFactory<ServiceDbContext>(options => options.UseSqlite(resolved));
            await using (var app = builder.Build())
            {
                MethodInfo initialize = database.GetMethod("InitializeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                await (Task)initialize.Invoke(null, [app, CancellationToken.None])!;
                await using var connection = new SqliteConnection(resolved);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE AssessmentSentinel(Value TEXT NOT NULL); INSERT INTO AssessmentSentinel VALUES ('preserved');";
                await command.ExecuteNonQueryAsync();
                await (Task)initialize.Invoke(null, [app, CancellationToken.None])!;
                command.CommandText = "SELECT Value FROM AssessmentSentinel";
                Assert.Equal("preserved", await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
