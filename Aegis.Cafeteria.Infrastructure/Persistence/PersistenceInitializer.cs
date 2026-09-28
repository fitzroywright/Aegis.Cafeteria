namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Initializes only the local POS SQLite database. Financial schema mismatches are rejected.
/// Additive FFP customer-directory cache fields, transaction identification metadata and imported legacy history may be added in place
/// because they do not rewrite native Aegis financial transactions.
/// </summary>
public sealed class PersistenceInitializer(
    IDbContextFactory<LocalCafeteriaDbContext> localFactory,
    ILogger<PersistenceInitializer> logger)
{
    public const int CurrentLocalSchemaVersion = 74;
    public const string CurrentProductVersion = "v74";

    public async Task InitializeLocalAsync(CancellationToken ct = default)
    {
        await using LocalCafeteriaDbContext db = await localFactory.CreateDbContextAsync(ct);

        bool databaseExisted = await DatabaseContainsAnyUserTableAsync(db, ct);
        if (!databaseExisted)
        {
            await db.Database.EnsureCreatedAsync(ct);
            db.LocalSchemaInfo.Add(new LocalSchemaInfoRow
            {
                Id = 1,
                SchemaVersion = CurrentLocalSchemaVersion,
                ProductVersion = CurrentProductVersion,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await UpgradeSchemaIfRequiredAsync(db, ct);
            await ValidateExistingDatabaseIsCurrentAsync(db, ct);
            await EnsureCustomerDirectoryColumnsAsync(db, ct);
            await EnsureOrderIdentificationColumnsAsync(db, ct);
            await EnsureExternalLedgerOutboxIdentificationColumnsAsync(db, ct);
        }

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=FULL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", ct);
        await EnsurePerformanceIndexesAsync(db, ct);

        logger.LogInformation(
            "Local SQLite persistence initialized at schema version {SchemaVersion} with WAL, FULL synchronous durability and foreign keys enabled",
            CurrentLocalSchemaVersion);
    }


    private static async Task EnsurePerformanceIndexesAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        string[] commands =
        [
            "CREATE INDEX IF NOT EXISTS \"IX_OrderItems_OrderId\" ON \"OrderItems\" (\"OrderId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_OrderPayments_OrderId\" ON \"OrderPayments\" (\"OrderId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_OrderRefunds_OrderId\" ON \"OrderRefunds\" (\"OrderId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_OrderRefundItems_RefundId\" ON \"OrderRefundItems\" (\"RefundId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_RefundAllocations_RefundId\" ON \"RefundAllocations\" (\"RefundId\");"
        ];

        foreach (string command in commands)
            await db.Database.ExecuteSqlRawAsync(command, ct);
    }

    private static async Task UpgradeSchemaIfRequiredAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        if (!await TableExistsAsync(db, "LocalSchemaInfo", ct))
            return;

        LocalSchemaInfoRow? schema = await db.LocalSchemaInfo
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        if (schema is null || schema.SchemaVersion == CurrentLocalSchemaVersion)
            return;

        if (schema.SchemaVersion != 73)
            return;

        bool oldTable = await TableExistsAsync(db, "FfpLedgerOutbox", ct);
        bool newTable = await TableExistsAsync(db, "ExternalLedgerOutbox", ct);

        if (oldTable && newTable)
            throw new InvalidOperationException(
                "Both legacy and current external ledger outbox tables exist. Back up the database and resolve the duplicate tables before continuing.");

        if (oldTable)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"FfpLedgerOutbox\" RENAME TO \"ExternalLedgerOutbox\";",
                ct);

        schema.SchemaVersion = CurrentLocalSchemaVersion;
        schema.ProductVersion = CurrentProductVersion;
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureCustomerDirectoryColumnsAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        HashSet<string> customerColumns = await ReadColumnsAsync(db, "Customers", ct);

        if (!customerColumns.Contains("AdditionalCreditPolicyUpdatedAt"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"AdditionalCreditPolicyUpdatedAt\" TEXT NULL;", ct);
        }

        if (!customerColumns.Contains("LoginName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"LoginName\" TEXT NOT NULL DEFAULT '';", ct);

        if (!customerColumns.Contains("Manager"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"Manager\" TEXT NOT NULL DEFAULT '';", ct);

        if (!customerColumns.Contains("EmailAddress"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"EmailAddress\" TEXT NOT NULL DEFAULT '';", ct);

        if (!customerColumns.Contains("Extension"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"Extension\" TEXT NOT NULL DEFAULT '';", ct);

        if (!customerColumns.Contains("IsActive"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"IsActive\" INTEGER NOT NULL DEFAULT 1;", ct);

        if (!customerColumns.Contains("EmploymentStartDate"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Customers\" ADD COLUMN \"EmploymentStartDate\" TEXT NULL;", ct);
    }

    private static async Task EnsureOrderIdentificationColumnsAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        HashSet<string> orderColumns = await ReadColumnsAsync(db, "Orders", ct);

        if (!orderColumns.Contains("IDMethod"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" ADD COLUMN \"IDMethod\" INTEGER NULL;", ct);

        if (!orderColumns.Contains("IdentificationCode"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" ADD COLUMN \"IdentificationCode\" TEXT NULL;", ct);

        if (!orderColumns.Contains("CashierName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" ADD COLUMN \"CashierName\" TEXT NULL;", ct);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_Orders_IDMethod_CompletedAt\" ON \"Orders\" (\"IDMethod\", \"CompletedAt\");",
            ct);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_Orders_Status_Revision\" ON \"Orders\" (\"Status\", \"Revision\");",
            ct);
    }

    private static async Task EnsureExternalLedgerOutboxIdentificationColumnsAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        if (!await TableExistsAsync(db, "ExternalLedgerOutbox", ct)) return;

        HashSet<string> columns = await ReadColumnsAsync(db, "ExternalLedgerOutbox", ct);
        if (!columns.Contains("Code"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ExternalLedgerOutbox\" ADD COLUMN \"Code\" TEXT NULL;", ct);

        if (!columns.Contains("CreatedBy"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ExternalLedgerOutbox\" ADD COLUMN \"CreatedBy\" TEXT NOT NULL DEFAULT 'SYSTEM';", ct);
    }

    private static async Task<bool> DatabaseContainsAnyUserTableAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        bool closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            await using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            object? value = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private static async Task ValidateExistingDatabaseIsCurrentAsync(LocalCafeteriaDbContext db, CancellationToken ct)
    {
        bool hasSchemaTable = await TableExistsAsync(db, "LocalSchemaInfo", ct);
        if (!hasSchemaTable) throw LegacyDatabaseException();

        LocalSchemaInfoRow? schema = await db.LocalSchemaInfo.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (schema is null || schema.SchemaVersion != CurrentLocalSchemaVersion)
            throw LegacyDatabaseException(schema?.SchemaVersion);

        HashSet<string> policyColumns = await ReadColumnsAsync(db, "CafeteriaRatePolicy", ct);
        foreach (string required in new[] { "CreditLimitLunchItemCode", "CreditLimitPrice", "SubsidyLunchItemCode", "SubsidyCost" })
            if (!policyColumns.Contains(required))
                throw new InvalidOperationException($"The local cafeteria database claims to be current but CafeteriaRatePolicy is missing {required}. Delete/archive the database and restart to create a clean database.");

        HashSet<string> allocationColumns = await ReadColumnsAsync(db, "MonthlyCreditAllocations", ct);
        foreach (string required in new[] { "CreditLimitPrice", "SubsidyCost", "TotalCreditLimit", "SubsidyAllocation" })
            if (!allocationColumns.Contains(required))
                throw new InvalidOperationException($"The local cafeteria database claims to be current but MonthlyCreditAllocations is missing {required}. Delete/archive the database and restart to create a clean database.");

        HashSet<string> customerColumns = await ReadColumnsAsync(db, "Customers", ct);
        if (!customerColumns.Contains("Department"))
            throw new InvalidOperationException(
                "The local cafeteria database claims to be current but Customers.Department is missing. " +
                "Delete/archive the database and restart to create a clean database.");

        HashSet<string> orderColumns = await ReadColumnsAsync(db, "Orders", ct);
        if (!orderColumns.Contains("CashierUserId") || !orderColumns.Contains("TerminalId") || !orderColumns.Contains("CompletedAt"))
            throw new InvalidOperationException(
                "The local cafeteria database claims to be current but its Orders table is incomplete. " +
                "Do not continue using this file. Back it up and create a fresh database using the documented manual procedure.");
    }

    private static InvalidOperationException LegacyDatabaseException(int? found = null) =>
        new(
            "A legacy Cafeteria local database was detected" +
            (found is null ? "." : $" (schema version {found}).") +
            " The application intentionally does not modify or delete legacy financial data automatically. " +
            "Back up the old SQLite database, stop the POS, manually remove/rename the old database file, and restart to create the current schema. " +
            "See docs/DATABASE-DEPLOYMENT.md.");

    private static async Task<bool> TableExistsAsync(LocalCafeteriaDbContext db, string table, CancellationToken ct)
    {
        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        bool closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            await using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            System.Data.Common.DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(LocalCafeteriaDbContext db, string table, CancellationToken ct)
    {
        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        bool closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone) await connection.OpenAsync(ct);
        try
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
            await using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(reader.GetString(1));
            return result;
        }
        finally
        {
            if (closeWhenDone) await connection.CloseAsync();
        }
    }
}
