namespace Aegis.Cafeteria.Services.Persistence;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

public sealed class AuditTrailService
{
    private static readonly SemaphoreSlim initializationLock = new(1, 1);
    private static readonly SemaphoreSlim appendLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> initializedDatabases = new(StringComparer.Ordinal);

    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly CafeteriaLoggingOptions loggingOptions;

    public AuditTrailService(IDbContextFactory<ServiceDbContext> factory)
        : this(factory, new CafeteriaLoggingOptions())
    {
    }

    public AuditTrailService
    (
          IDbContextFactory<ServiceDbContext> factory
        , CafeteriaLoggingOptions loggingOptions
    )
    {
        this.factory = factory;
        this.loggingOptions = loggingOptions;
    }

    public async Task<AuditTrailRow> RecordAsync
    (
          string actor
        , string action
        , string entityType
        , string? entityId = null
        , int? customerId = null
        , Guid? operationId = null
        , string source = "Aegis.Cafeteria.Services"
        , string? terminalId = null
        , string? correlationId = null
        , bool succeeded = true
        , int? statusCode = null
        , object? before = null
        , object? after = null
        , string? detail = null
        , CancellationToken cancellationToken = default
    )
    {
        await EnsureTableAsync(cancellationToken);
        await appendLock.WaitAsync(cancellationToken);

        try
        {
            await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
            AuditTrailRow? previous = await database.AuditTrail
                .AsNoTracking()
                .Where(row => row.Hash != null)
                .OrderByDescending(row => row.Id)
                .FirstOrDefaultAsync(cancellationToken);

            AuditTrailRow row = new()
            {
                  OccurredAt = DateTimeOffset.UtcNow
                , Actor = NormalizeActor(actor)
                , Action = action.Trim()
                , EntityType = entityType.Trim()
                , EntityId = NullIfWhiteSpace(entityId)
                , CustomerId = customerId
                , OperationId = operationId
                , Source = source.Trim()
                , TerminalId = NullIfWhiteSpace(terminalId)
                , CorrelationId = NullIfWhiteSpace(correlationId)
                , Succeeded = succeeded
                , StatusCode = statusCode
                , BeforeJson = SerializeSafe(before)
                , AfterJson = SerializeSafe(after)
                , Detail = Truncate(detail, 2000)
                , PreviousHash = previous?.Hash
            };

            row.Hash = ComputeHash(row);
            database.AuditTrail.Add(row);
            await database.SaveChangesAsync(cancellationToken);

            if (this.loggingOptions.GraylogEnabled)
            {
                _ = SendToGraylogAsync(row);
            }

            return row;
        }
        finally
        {
            appendLock.Release();
        }
    }

    public async Task<AuditIntegrityResult> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);

        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        AuditTrailRow[] rows = await database.AuditTrail
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        string? previousHash = null;
        int legacyRows = 0;
        int verifiedRows = 0;

        foreach (AuditTrailRow row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Hash))
            {
                legacyRows++;
                continue;
            }

            if (!string.Equals(row.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return new AuditIntegrityResult(false, verifiedRows, legacyRows, row.Id, "Previous hash does not match the preceding chained audit row.");
            }

            string expectedHash = ComputeHash(row);
            if (!string.Equals(row.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditIntegrityResult(false, verifiedRows, legacyRows, row.Id, "Audit row hash does not match its persisted content.");
            }

            previousHash = row.Hash;
            verifiedRows++;
        }

        return new AuditIntegrityResult(true, verifiedRows, legacyRows, null, null);
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        string databaseKey = database.Database.GetConnectionString()
            ?? database.Database.GetDbConnection().DataSource
            ?? "default";

        if (initializedDatabases.ContainsKey(databaseKey))
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initializedDatabases.ContainsKey(databaseKey))
            {
                return;
            }

            await database.Database.ExecuteSqlRawAsync
            (
                @"CREATE TABLE IF NOT EXISTS AuditTrail (
                      Id INTEGER NOT NULL CONSTRAINT PK_AuditTrail PRIMARY KEY AUTOINCREMENT
                    , OccurredAt TEXT NOT NULL
                    , Actor TEXT NOT NULL
                    , Action TEXT NOT NULL
                    , EntityType TEXT NOT NULL
                    , EntityId TEXT NULL
                    , CustomerId INTEGER NULL
                    , OperationId TEXT NULL
                    , Source TEXT NOT NULL
                    , TerminalId TEXT NULL
                    , CorrelationId TEXT NULL
                    , Succeeded INTEGER NOT NULL
                    , StatusCode INTEGER NULL
                    , BeforeJson TEXT NULL
                    , AfterJson TEXT NULL
                    , Detail TEXT NULL
                    , PreviousHash TEXT NULL
                    , Hash TEXT NULL
                );",
                cancellationToken
            );

            await EnsureColumnAsync(database, "PreviousHash", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(database, "Hash", "TEXT NULL", cancellationToken);

            await database.Database.ExecuteSqlRawAsync
            (
                @"CREATE INDEX IF NOT EXISTS IX_AuditTrail_OccurredAt ON AuditTrail (OccurredAt);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_Actor ON AuditTrail (Actor);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_EntityType_EntityId ON AuditTrail (EntityType, EntityId);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_CustomerId ON AuditTrail (CustomerId);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_OperationId ON AuditTrail (OperationId);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_AuditTrail_Hash ON AuditTrail (Hash) WHERE Hash IS NOT NULL;",
                cancellationToken
            );

            initializedDatabases[databaseKey] = 1;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static async Task EnsureColumnAsync
    (
          ServiceDbContext database
        , string columnName
        , string definition
        , CancellationToken cancellationToken
    )
    {
        System.Data.Common.DbConnection connection = database.Database.GetDbConnection();
        bool closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            bool exists = false;
            await using System.Data.Common.DbCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(AuditTrail);";
            await using System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                await reader.DisposeAsync();
                await database.Database.ExecuteSqlRawAsync($"ALTER TABLE AuditTrail ADD COLUMN {columnName} {definition};", cancellationToken);
            }
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task SendToGraylogAsync(AuditTrailRow row)
    {
        try
        {
            Dictionary<string, object?> payload = new()
            {
                  ["version"] = "1.1"
                , ["host"] = Environment.MachineName
                , ["short_message"] = $"AUDIT {row.Action} {row.EntityType} {row.EntityId}".Trim()
                , ["timestamp"] = row.OccurredAt.ToUnixTimeMilliseconds() / 1000d
                , ["level"] = row.Succeeded ? 6 : 4
                , ["_application"] = "Aegis.Cafeteria.Services"
                , ["_audit"] = true
                , ["_audit_id"] = row.Id
                , ["_actor"] = row.Actor
                , ["_action"] = row.Action
                , ["_entity_type"] = row.EntityType
                , ["_entity_id"] = row.EntityId
                , ["_customer_id"] = row.CustomerId
                , ["_operation_id"] = row.OperationId?.ToString()
                , ["_source"] = row.Source
                , ["_terminal_id"] = row.TerminalId
                , ["_correlation_id"] = row.CorrelationId
                , ["_succeeded"] = row.Succeeded
                , ["_status_code"] = row.StatusCode
                , ["_before"] = row.BeforeJson
                , ["_after"] = row.AfterJson
                , ["_detail"] = row.Detail
                , ["_previous_hash"] = row.PreviousHash
                , ["_hash"] = row.Hash
            };

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(this.loggingOptions.GraylogHost);
            IPAddress address = addresses.FirstOrDefault() ?? IPAddress.Parse(this.loggingOptions.GraylogHost);
            IPEndPoint endpoint = new(address, this.loggingOptions.GraylogPort);

            using UdpClient client = new();
            await client.SendAsync(bytes, endpoint);
        }
        catch
        {
            // The database audit row is authoritative. Graylog availability must never affect the transaction.
        }
    }

    private static string ComputeHash(AuditTrailRow row)
    {
        string canonical = string.Join
        (
            "\u001f",
            row.PreviousHash ?? string.Empty,
            row.OccurredAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            row.Actor,
            row.Action,
            row.EntityType,
            row.EntityId ?? string.Empty,
            row.CustomerId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row.OperationId?.ToString("D") ?? string.Empty,
            row.Source,
            row.TerminalId ?? string.Empty,
            row.CorrelationId ?? string.Empty,
            row.Succeeded ? "1" : "0",
            row.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row.BeforeJson ?? string.Empty,
            row.AfterJson ?? string.Empty,
            row.Detail ?? string.Empty
        );

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeActor(string actor)
    {
        return string.IsNullOrWhiteSpace(actor) ? "SYSTEM" : Truncate(actor.Trim(), 256)!;
    }

    private static string? SerializeSafe(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string json = JsonSerializer.Serialize(value);
        return Truncate(json, 8000);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
    }
}

public sealed record AuditIntegrityResult
(
      bool IsValid
    , int VerifiedRows
    , int LegacyRows
    , long? FirstInvalidAuditId
    , string? Error
);
