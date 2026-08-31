namespace Aegis.Cafeteria.Domain.Reporting;

public sealed record CustomerAccountStatementEntry(
    DateTimeOffset OccurredAt,
    string Category,
    string Description,
    decimal Amount,
    Guid? ReferenceId = null);
