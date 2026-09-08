namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Domain.Accounts;

/// <summary>
/// Management callers submit the business data only. ApprovedBy/ProcessedBy are deliberately
/// not accepted from the request body; Aegis.Cafeteria.Services derives the actor from the
/// authenticated Windows/AD principal so the audit identity cannot be spoofed by a client.
/// </summary>
public sealed record AdditionalCreditRequest(
    int CustomerId,
    decimal Amount,
    string? Reason,
    DateTimeOffset? ApprovedAt = null,
    Guid? OperationId = null);

public sealed record StipendRequest(
    int CustomerId,
    decimal CashAmount,
    decimal CardReductionAmount,
    StipendPaymentMode PaymentMode,
    string? ExpenseReportReference = null,
    string? Notes = null,
    DateTimeOffset? ProcessedAt = null,
    Guid? OperationId = null);

public sealed record CustomerCreditPolicyRequest(decimal AdditionalCreditLimit);
