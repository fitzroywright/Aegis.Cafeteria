namespace Aegis.Cafeteria.Application.Abstractions;

public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically claims an operation ID. Returns false when the operation has already
    /// been claimed or completed by another execution.
    /// </summary>
    Task<bool> TryBeginAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Releases a failed claim so a later retry may execute the operation.</summary>
    Task AbandonAsync(Guid operationId, CancellationToken cancellationToken = default);
}
