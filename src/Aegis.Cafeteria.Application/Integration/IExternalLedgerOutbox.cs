namespace Aegis.Cafeteria.Application.Integration;

public interface IExternalLedgerOutbox
{
    Task EnqueueAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default);
}
