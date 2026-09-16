using Aegis.Cafeteria.Domain.Accounts;

namespace Aegis.Cafeteria.Services;

public sealed record StipendRequest(
    int CustomerId,
    decimal CashAmount,
    decimal CardReductionAmount,
    StipendPaymentMode PaymentMode,
    string? ExpenseReportReference = null,
    string? Notes = null,
    DateTimeOffset? ProcessedAt = null,
    Guid? OperationId = null,
    int? FundingYear = null,
    int? FundingMonth = null);
