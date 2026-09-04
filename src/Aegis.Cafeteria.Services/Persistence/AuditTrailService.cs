namespace Aegis.Cafeteria.Services.Persistence;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

public sealed class AuditTrailService
{
    private static readonly SemaphoreSlim initializationLock = new(1, 1);
    private static bool initialized;

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
        };

        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        database.AuditTrail.Add(row);
        await database.SaveChangesAsync(cancellationToken);

        if (this.loggingOptions.GraylogEnabled)
        {
            _ = SendToGraylogAsync(row);
        }

        return row;
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
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
                );
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_OccurredAt ON AuditTrail (OccurredAt);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_Actor ON AuditTrail (Actor);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_EntityType_EntityId ON AuditTrail (EntityType, EntityId);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_CustomerId ON AuditTrail (CustomerId);
                CREATE INDEX IF NOT EXISTS IX_AuditTrail_OperationId ON AuditTrail (OperationId);",
                cancellationToken
            );

            initialized = true;
        }
        finally
        {
            initializationLock.Release();
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
