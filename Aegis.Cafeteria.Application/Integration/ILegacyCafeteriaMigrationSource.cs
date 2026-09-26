namespace Aegis.Cafeteria.Application.Integration;

public interface ILegacyCafeteriaMigrationSource
{
    Task<LegacyCafeteriaMonthSnapshot> ReadMonthAsync(
        string connectionString,
        int year,
        int month,
        DateTimeOffset throughAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LegacyCafeteriaProductSnapshot>> ReadProductsAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyCafeteriaMonthSnapshot(
    int Year,
    int Month,
    DateTimeOffset ThroughAt,
    IReadOnlyList<LegacyCafeteriaTransactionSnapshot> Transactions,
    IReadOnlyList<LegacyCafeteriaAccountSnapshot> Accounts);

public sealed record LegacyCafeteriaTransactionSnapshot(
    long SourceId,
    int CustomerId,
    DateTimeOffset OccurredAt,
    string IdName,
    string Description,
    decimal ScanValue,
    string UserName,
    string ScanCode,
    int ScanType);

public sealed record LegacyCafeteriaAccountSnapshot(
    int CustomerId,
    decimal CreditLimit,
    decimal AvailableBalance);

public sealed record LegacyCafeteriaProductSnapshot(
    int ProductId,
    string Code,
    string Name,
    decimal Price);
