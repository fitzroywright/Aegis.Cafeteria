namespace Aegis.Cafeteria.Application.Integration;

/// <summary>Anti-corruption boundary for external workforce and holiday reference data.</summary>
public interface IWorkforceReferenceSource
{
    Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default);
}

public sealed record ExternalCustomerSnapshot
{
    public int CustomerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsEligibleForCafeteriaCredit { get; init; }
    public bool IsActive { get; init; } = true;
    public DateOnly? EmploymentStartDate { get; init; }
    public string? Nfc { get; init; }
    public string? Code { get; init; }
    public string? Trn { get; init; }
    public byte[]? Photo { get; init; }
    public string Department { get; init; } = string.Empty;
    public decimal AdditionalCreditLimit { get; init; }
    public string Manager { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string LoginName { get; init; } = string.Empty;
}

public interface IWorkforceReferenceCache
{
    Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, CancellationToken cancellationToken = default);
    Task ReplaceHolidaysAsync(int year, int month, IReadOnlyCollection<DateOnly> holidays, CancellationToken cancellationToken = default);
}

/// <summary>Bounded employee reads using the last employee ID as a cursor.</summary>
public interface IPagedWorkforceReferenceSource
{
    int CustomerBatchSize => 10;
    Task<CustomerReferenceBatch> GetCustomerBatchAsync(int? afterCustomerId, int take, CancellationToken cancellationToken = default);
}

public sealed record CustomerReferenceBatch
(
      IReadOnlyList<ExternalCustomerSnapshot> Customers
    , int? NextCustomerId
);

public interface IBatchedWorkforceReferenceCache
{
    Task UpsertCustomerBatchAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, IReadOnlySet<int> previouslyRefreshedIds, CancellationToken cancellationToken = default);
    Task CompleteCustomerRefreshAsync(IReadOnlySet<int> customerIds, CancellationToken cancellationToken = default);
}
