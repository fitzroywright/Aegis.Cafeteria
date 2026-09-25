namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class LocalSchemaBaselineTests
{
    [Fact]
    public async Task NewDatabase_CreatesCompleteV74LocalOnlySchema()
    {
        var file = SqliteTestDatabase.CreatePath("v74-schema");
        try
        {
            SqliteTestDatabase.Delete(file);
            var factory = new LocalDbContextFactory(
                DbContextOptionsFactory.CreateLocal(
                    new CafeteriaPersistenceOptions
                    {
                        LocalSqliteConnectionString = $"Data Source={file}"
                    }));

            await new PersistenceInitializer(
                factory,
                NullLogger<PersistenceInitializer>.Instance)
                .InitializeLocalAsync();

            await using var db = factory.CreateDbContext();
            var schema = Assert.Single(await db.LocalSchemaInfo.ToListAsync());

            Assert.Equal(74, schema.SchemaVersion);
            Assert.Equal("v74", schema.ProductVersion);

            foreach (var table in new[]
            {
                "Customers",
                "CustomerIdentifiers",
                "Products",
                "Orders",
                "ExternalLedgerOutbox",
                "CafeteriaRatePolicy",
                "MonthlyCreditAllocations",
                "WeeklySummaryDispatches",
                "ManagementEventInbox"
            })
            {
                Assert.True(await TableExists(file, table));
            }

            Assert.False(await TableExists(file, "FfpLedgerOutbox"));

            foreach (var removed in new[]
            {
                "CustomerFundingProfiles",
                "Outbox",
                "SyncState",
                "Inbox",
                "ChangeJournal"
            })
            {
                Assert.False(await TableExists(file, removed));
            }
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task ExistingV73Database_RenamesOutboxAndPreservesRows()
    {
        var file = SqliteTestDatabase.CreatePath("v73-upgrade");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={file}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE LocalSchemaInfo (
    Id INTEGER NOT NULL PRIMARY KEY,
    SchemaVersion INTEGER NOT NULL,
    ProductVersion TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
INSERT INTO LocalSchemaInfo VALUES (1, 73, 'v73', CURRENT_TIMESTAMP);

CREATE TABLE FfpLedgerOutbox (
    OperationId TEXT NOT NULL PRIMARY KEY,
    EmployeeId INTEGER NOT NULL,
    Kind INTEGER NOT NULL,
    OccurredAt TEXT NOT NULL,
    Description TEXT NOT NULL,
    ScanValue TEXT NOT NULL,
    CreditLimit TEXT NOT NULL,
    AvailableBalance TEXT NOT NULL,
    ScanCode TEXT NOT NULL,
    UserName TEXT NOT NULL,
    IdName TEXT NULL,
    Code TEXT NULL,
    CreatedBy TEXT NOT NULL,
    State INTEGER NOT NULL,
    AttemptCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    LastAttemptAt TEXT NULL,
    LastError TEXT NULL
);
INSERT INTO FfpLedgerOutbox (
    OperationId, EmployeeId, Kind, OccurredAt, Description, ScanValue,
    CreditLimit, AvailableBalance, ScanCode, UserName, IdName, Code,
    CreatedBy, State, AttemptCount, CreatedAt
) VALUES (
    '00000000-0000-0000-0000-000000000001', 42, 1, CURRENT_TIMESTAMP,
    'Preserve me', '500', '5000', '4500', 'NFC', 'Cashier',
    'Purchase', 'CARD42', 'SYSTEM', 0, 0, CURRENT_TIMESTAMP
);

CREATE TABLE Customers (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', NormalizedName TEXT NOT NULL DEFAULT '', Department TEXT NOT NULL DEFAULT '', Manager TEXT NOT NULL DEFAULT '', EmailAddress TEXT NOT NULL DEFAULT '', Extension TEXT NOT NULL DEFAULT '', IsActive INTEGER NOT NULL DEFAULT 1, EmploymentStartDate TEXT NULL, LoginName TEXT NOT NULL DEFAULT '');
CREATE TABLE CustomerIdentifiers (Id TEXT NOT NULL PRIMARY KEY, CustomerId INTEGER NOT NULL, Type INTEGER NOT NULL, Value TEXT NOT NULL DEFAULT '', NormalizedValue TEXT NOT NULL DEFAULT '');
CREATE TABLE CafeteriaHolidayCache (Date TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL DEFAULT '');
CREATE TABLE CafeteriaRatePolicy (Id INTEGER NOT NULL PRIMARY KEY, CreditLimitLunchItemCode TEXT NOT NULL DEFAULT '', CreditLimitPrice TEXT NOT NULL DEFAULT '0', SubsidyLunchItemCode TEXT NOT NULL DEFAULT '', SubsidyCost TEXT NOT NULL DEFAULT '0');
CREATE TABLE Products (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL DEFAULT '');
CREATE TABLE Orders (Id TEXT NOT NULL PRIMARY KEY, CustomerId INTEGER NOT NULL, AccountYear INTEGER NOT NULL, AccountMonth INTEGER NOT NULL, CashierUserId TEXT NULL, CashierName TEXT NULL, TerminalId TEXT NULL, CompletedAt TEXT NULL, IDMethod INTEGER NULL, IdentificationCode TEXT NULL);
CREATE TABLE MonthlyCreditAllocations (CustomerId INTEGER NOT NULL, Year INTEGER NOT NULL, Month INTEGER NOT NULL, CreditLimitPrice TEXT NOT NULL DEFAULT '0', SubsidyCost TEXT NOT NULL DEFAULT '0', TotalCreditLimit TEXT NOT NULL DEFAULT '0', SubsidyAllocation TEXT NOT NULL DEFAULT '0', PRIMARY KEY(CustomerId, Year, Month));
";
                await command.ExecuteNonQueryAsync();
            }

            var factory = new LocalDbContextFactory(
                DbContextOptionsFactory.CreateLocal(
                    new CafeteriaPersistenceOptions
                    {
                        LocalSqliteConnectionString = $"Data Source={file}"
                    }));

            await new PersistenceInitializer(
                factory,
                NullLogger<PersistenceInitializer>.Instance)
                .InitializeLocalAsync();

            Assert.False(await TableExists(file, "FfpLedgerOutbox"));
            Assert.True(await TableExists(file, "ExternalLedgerOutbox"));

            await using var connection2 = new SqliteConnection($"Data Source={file}");
            await connection2.OpenAsync();
            var countCommand = connection2.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM ExternalLedgerOutbox WHERE Description='Preserve me';";
            Assert.Equal(1L, (long)(await countCommand.ExecuteScalarAsync())!);

            await using var db = factory.CreateDbContext();
            var schema = Assert.Single(await db.LocalSchemaInfo.ToListAsync());
            Assert.Equal(74, schema.SchemaVersion);
            Assert.Equal("v74", schema.ProductVersion);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task ExistingNonCurrentDatabase_IsRejectedAndNeverDeleted()
    {
        var file = SqliteTestDatabase.CreatePath("legacy");
        try
        {
            await using (var c = new SqliteConnection($"Data Source={file}"))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE Orders(Id TEXT PRIMARY KEY); INSERT INTO Orders VALUES('keep-me');";
                await cmd.ExecuteNonQueryAsync();
            }

            var factory = new LocalDbContextFactory(
                DbContextOptionsFactory.CreateLocal(
                    new CafeteriaPersistenceOptions
                    {
                        LocalSqliteConnectionString = $"Data Source={file}"
                    }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PersistenceInitializer(
                    factory,
                    NullLogger<PersistenceInitializer>.Instance)
                .InitializeLocalAsync());

            Assert.Contains("manually", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(file));
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private static async Task<bool> TableExists(string file, string table)
    {
        await using var c = new SqliteConnection($"Data Source={file}");
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return (long)(await cmd.ExecuteScalarAsync())! == 1L;
    }
}
