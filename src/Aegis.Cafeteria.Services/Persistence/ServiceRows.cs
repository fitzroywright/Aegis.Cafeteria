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
