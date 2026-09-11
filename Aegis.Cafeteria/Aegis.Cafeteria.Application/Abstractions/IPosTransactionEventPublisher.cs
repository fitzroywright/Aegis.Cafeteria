namespace Aegis.Cafeteria.Application.Abstractions;

public interface IPosTransactionEventPublisher
{
    Task PublishAsync(PosTransactionEvent transactionEvent, CancellationToken cancellationToken = default);
}

public sealed record PosTransactionEvent
(
      Guid EventId
    , Guid CorrelationId
    , string Kind
    , Guid OrderId
    , int CustomerId
    , string Actor
    , string? TerminalId
    , int AccountYear
    , int AccountMonth
    , DateTimeOffset OccurredAt
    , string PayloadJson
    , string? Reason
);
