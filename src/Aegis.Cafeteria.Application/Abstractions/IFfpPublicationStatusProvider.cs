namespace Aegis.Cafeteria.Application.Abstractions;
public interface IFfpPublicationStatusProvider { Task<FfpPublicationStatus> GetAsync(CancellationToken cancellationToken=default); }
public sealed record FfpPublicationStatus(bool Enabled,int PendingOperations,int PoisonedOperations,DateTimeOffset? LastSuccessfulPublication,string? LastError);
