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

    // Service-originated financial events are published directly to FFP by the service. These
    // fields make publication durable and retryable independently of whether any POS is online.
    public DateTimeOffset? FfpPublishedAt { get; set; }
    public int FfpAttemptCount { get; set; }
    public DateTimeOffset? FfpLastAttemptAt { get; set; }
    public string? FfpLastError { get; set; }
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

public sealed class CafeteriaFundingPolicyRow
{
    public int Id { get; set; }
    public decimal DailyCreditLimitRate { get; set; }
    public decimal DailyCompanySubsidyRate { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CafeteriaFundingPolicyHistoryRow
{
    public long Id { get; set; }
    public decimal DailyCreditLimitRate { get; set; }
    public decimal DailyCompanySubsidyRate { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
