namespace Aegis.Cafeteria.Application.Integration;

public sealed record CustomerReferenceCacheStatus(
    int EmployeeCount,
    int IdentifierCount,
    DateTimeOffset? CacheUpdatedAtUtc,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    bool RefreshInProgress)
{
    public int RefreshedEmployeeCount { get; init; }
    public int CompletedBatchCount { get; init; }

    public TimeSpan? Age(DateTimeOffset nowUtc)
        => CacheUpdatedAtUtc.HasValue ? nowUtc - CacheUpdatedAtUtc.Value : null;
}

public interface ICustomerReferenceCacheStatus
{
    Task<CustomerReferenceCacheStatus> GetAsync(CancellationToken cancellationToken = default);
    void RefreshStarted();
    void RefreshProgress(int employeeCount, int batchCount) { }
    void RefreshSucceeded(int employeeCount);
    void RefreshFailed(Exception exception);
}
