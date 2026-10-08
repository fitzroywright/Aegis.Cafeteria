namespace Aegis.Cafeteria.Services;
public sealed record StipendDebitRequest(int CustomerId, decimal Amount, string? Reason, Guid OperationId);
