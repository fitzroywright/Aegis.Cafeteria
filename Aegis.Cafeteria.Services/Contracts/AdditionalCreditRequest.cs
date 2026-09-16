namespace Aegis.Cafeteria.Services;

public sealed record AdditionalCreditRequest(
    int CustomerId,
    decimal Amount,
    string? Reason,
    DateTimeOffset? ApprovedAt = null,
    Guid? OperationId = null);
