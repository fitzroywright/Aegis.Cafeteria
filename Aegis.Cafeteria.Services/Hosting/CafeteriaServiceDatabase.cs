using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace Aegis.Cafeteria.Services;

internal static class CafeteriaServiceDatabase
{
    internal static string ResolveConnectionString(IConfiguration configuration)
    {
        string connectionString = configuration["ServicePersistence:ConnectionString"] ?? "Data Source=cafeteria.services.db";
        EnsureDatabaseDirectory(connectionString);
        return connectionString;
    }

    internal static async Task InitializeAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        IDbContextFactory<ServiceDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
        await using ServiceDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureSupplementalSchemaAsync(db, cancellationToken);
    }

    private static void EnsureDatabaseDirectory(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:") return;
        string fullPath = Path.GetFullPath(builder.DataSource);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static Task EnsureSupplementalSchemaAsync(ServiceDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS CustomerNotificationPreferences (
                  CustomerId INTEGER NOT NULL CONSTRAINT PK_CustomerNotificationPreferences PRIMARY KEY
                , PreferenceAsked INTEGER NOT NULL
                , WeeklySummaryEnabled INTEGER NOT NULL
                , EmailAddress TEXT NULL
                , UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ManagerReportSettings (
                  Id INTEGER NOT NULL CONSTRAINT PK_ManagerReportSettings PRIMARY KEY
                , WeeklySummaryEnabled INTEGER NOT NULL
                , EmailAddress TEXT NULL
                , WeeklySummaryDay INTEGER NOT NULL
                , WeeklySummaryHourLocal INTEGER NOT NULL
                , UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS OutboundEmails (
                  Id TEXT NOT NULL CONSTRAINT PK_OutboundEmails PRIMARY KEY
                , DispatchKey TEXT NOT NULL
                , Recipient TEXT NOT NULL
                , Subject TEXT NOT NULL
                , Body TEXT NOT NULL
                , AttemptCount INTEGER NOT NULL
                , CreatedAt TEXT NOT NULL
                , NextAttemptAt TEXT NOT NULL
                , LastAttemptAt TEXT NULL
                , SentAt TEXT NULL
                , MessageId TEXT NULL
                , LastError TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboundEmails_DispatchKey ON OutboundEmails (DispatchKey);
            CREATE INDEX IF NOT EXISTS IX_OutboundEmails_SentAt_NextAttemptAt ON OutboundEmails (SentAt, NextAttemptAt);

            CREATE TABLE IF NOT EXISTS IntegrationEmployeeCache (
                  EmployeeId INTEGER NOT NULL CONSTRAINT PK_IntegrationEmployeeCache PRIMARY KEY
                , Name TEXT NOT NULL
                , CardIdentifier TEXT NULL
                , IsActive INTEGER NOT NULL
                , IsEligible INTEGER NOT NULL
                , EligibleFrom TEXT NULL
                , Version TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS IntegrationState (
                  Key TEXT NOT NULL CONSTRAINT PK_IntegrationState PRIMARY KEY
                , Value TEXT NULL
                , UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ValidatedPolicyCache (
                  Year INTEGER NOT NULL
                , Month INTEGER NOT NULL
                , PayloadJson TEXT NOT NULL
                , ValidatedAt TEXT NOT NULL
                , CONSTRAINT PK_ValidatedPolicyCache PRIMARY KEY (Year, Month)
            );",
            cancellationToken);
}
