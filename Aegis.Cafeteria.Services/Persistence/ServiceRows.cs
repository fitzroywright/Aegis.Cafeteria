namespace Aegis.Cafeteria.Services.Persistence;

public sealed class ServiceEventRow
{
    public long Sequence { get; set; }
    public Guid OperationId { get; set; }
    public int Kind { get; set; }
    public int CustomerId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Service-originated financial events are published through the configured integration path.
    // These fields make publication durable and retryable independently of whether any POS is online.
    public DateTimeOffset? ExternalPublishedAt { get; set; }
    public int PublicationAttemptCount { get; set; }
    public DateTimeOffset? PublicationLastAttemptAt { get; set; }
    public string? PublicationLastError { get; set; }
}

public sealed class AuditTrailRow
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public int? CustomerId { get; set; }
    public Guid? OperationId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? TerminalId { get; set; }
    public string? CorrelationId { get; set; }
    public bool Succeeded { get; set; }
    public int? StatusCode { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Detail { get; set; }
    public string? PreviousHash { get; set; }
    public string? Hash { get; set; }
}

/// <summary>
/// Management-owned per-employee financial policy. Additional Credit is intentionally not governed
/// by one global application cap: the employee-specific limit is the domain rule enforced by
/// AdditionalCreditService.
/// </summary>
public sealed class CustomerCreditPolicyRow
{
    public int CustomerId { get; set; }
    public decimal AdditionalCreditLimit { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ScheduledDispatchRow
{
    public string DispatchKey { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public string? MessageId { get; set; }
}

public sealed class CustomerNotificationPreferenceRow
{
    public int CustomerId { get; set; }
    public bool PreferenceAsked { get; set; }
    public bool WeeklySummaryEnabled { get; set; }
    public string? EmailAddress { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ManagerReportSettingsRow
{
    public int Id { get; set; }
    public bool WeeklySummaryEnabled { get; set; }
    public string? EmailAddress { get; set; }
    public DayOfWeek WeeklySummaryDay { get; set; } = DayOfWeek.Monday;
    public int WeeklySummaryHourLocal { get; set; } = 8;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OutboundEmailRow
{
    public Guid Id { get; set; }
    public string DispatchKey { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? MessageId { get; set; }
    public string? LastError { get; set; }
}


public sealed class IntegrationEmployeeCacheRow
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CardIdentifier { get; set; }
    public bool IsActive { get; set; }
    public bool IsEligible { get; set; }
    public DateOnly? EligibleFrom { get; set; }
    public string Version { get; set; } = string.Empty;
}

public sealed class IntegrationStateRow
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ValidatedPolicyCacheRow
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset ValidatedAt { get; set; }
}
