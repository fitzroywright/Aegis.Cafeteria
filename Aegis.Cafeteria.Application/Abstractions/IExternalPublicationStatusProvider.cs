namespace Aegis.Cafeteria.Application.Abstractions;
public interface IExternalPublicationStatusProvider { Task<ExternalPublicationStatus> GetAsync(CancellationToken cancellationToken=default); }
public sealed record ExternalPublicationStatus(bool Enabled,int PendingOperations,int PoisonedOperations,DateTimeOffset? LastSuccessfulPublication,string? LastError);
