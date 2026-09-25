namespace Aegis.Cafeteria.Application.Integration;

public sealed record ExternalTransactionSnapshot
(
      long SourceId
    , int CustomerId
    , DateTimeOffset OccurredAt
    , string IdName
    , string Description
    , decimal ScanValue
    , string UserName
    , string ScanCode
    , int ScanType
);

public sealed record ExternalAccountSnapshot
(
      int CustomerId
    , decimal CreditLimit
    , decimal AvailableBalance
);

public sealed record MonthImportSnapshot
(
      int Year
    , int Month
    , DateTimeOffset ThroughAt
    , IReadOnlyList<ExternalTransactionSnapshot> Transactions
    , IReadOnlyList<ExternalAccountSnapshot> Accounts
);

public sealed record MonthImportResult
(
      int Year
    , int Month
    , DateTimeOffset ThroughAt
    , int ImportedTransactions
    , int ImportedAccounts
    , decimal NetImportedScanValue
);

public interface IMonthImportSource
{
    Task<MonthImportSnapshot> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}

public interface IMonthImportService
{
    Task<MonthImportResult> ImportAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}
