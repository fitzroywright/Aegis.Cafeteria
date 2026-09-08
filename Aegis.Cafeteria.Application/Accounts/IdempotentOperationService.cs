namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;

public sealed class IdempotentOperationService(IIdempotencyStore store)
{
    public async Task<bool> ExecuteAsync(Guid operationId, Func<Task> operation, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(operationId));
        ArgumentNullException.ThrowIfNull(operation);

        if (!await store.TryBeginAsync(operationId, cancellationToken))
            return false;

        try
        {
            await operation();
            await store.CompleteAsync(operationId, cancellationToken);
            return true;
        }
        catch
        {
            await store.AbandonAsync(operationId, cancellationToken);
            throw;
        }
    }
}
