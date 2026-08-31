namespace Aegis.Cafeteria.Application.Integration;

/// <summary>Anti-corruption boundary for FFP employee and holiday reference data. Aegis has no lunch-ticket concept.</summary>
public interface IWorkforceReferenceSource
{
    Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default);
}

public sealed record ExternalCustomerSnapshot(
    int CustomerId,
    string Name,
    bool IsEligibleForCafeteriaCredit,
    string? Nfc,
    string? Code,
    string? Trn,
    byte[]? Photo,
    string Department = "",
    decimal AdditionalCreditLimit = 0m,
    string Manager = "",
    string EmailAddress = "",
    string Extension = "");

public interface IWorkforceReferenceCache
{
    Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, CancellationToken cancellationToken = default);
    Task ReplaceHolidaysAsync(int year, int month, IReadOnlyCollection<DateOnly> holidays, CancellationToken cancellationToken = default);
}
