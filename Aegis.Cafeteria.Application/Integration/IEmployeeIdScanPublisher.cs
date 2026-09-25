namespace Aegis.Cafeteria.Application.Integration;

public interface IEmployeeIdScanPublisher
{
    Task PublishAsync(
        CafeteriaOutboundTransaction transaction,
        CancellationToken cancellationToken = default);
}

public sealed record CafeteriaOutboundTransaction(
    Guid TransactionId,
    int EmployeeId,
    string TransactionKind,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Description,
    string IdentificationMethod,
    string UserName,
    string? IdentificationCode = null);
