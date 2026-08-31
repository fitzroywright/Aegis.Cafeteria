namespace Aegis.Cafeteria.Domain.Services;

using Aegis.Cafeteria.Domain.Accounts;

public enum CafeteriaServiceEventKind
{
    AdditionalCredit = 1,
    Stipend = 2,
    MonthlyAllocation = 3
}

public sealed record CafeteriaServiceEventEnvelope(
    long Sequence,
    Guid OperationId,
    CafeteriaServiceEventKind Kind,
    int CustomerId,
    DateTimeOffset OccurredAt,
    string PayloadJson);

public sealed record AdditionalCreditServiceEvent(
    Guid OperationId,
    int CustomerId,
    decimal Amount,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string? Reason,
    decimal AdditionalCreditLimit);

public sealed record StipendServiceEvent(
    Guid OperationId,
    int CustomerId,
    decimal CashAmount,
    decimal CardReductionAmount,
    StipendPaymentMode PaymentMode,
    string ProcessedBy,
    DateTimeOffset ProcessedAt,
    string? ExpenseReportReference,
    string? Notes);

public sealed record MonthlyAllocationServiceEvent(
    Guid OperationId,
    int CustomerId,
    int Year,
    int Month,
    int EligibleWorkingDays,
    bool IsEligibleForCredit,
    decimal CreditLimitPrice,
    decimal SubsidyCost,
    DateTimeOffset CalculatedAt,
    string EmployeeName);
