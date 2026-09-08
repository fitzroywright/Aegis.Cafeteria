namespace Aegis.Cafeteria.Application.Integration;

public sealed record ExternalFfpTransactionSnapshot
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

public sealed record ExternalFfpAccountSnapshot
(
      int CustomerId
    , decimal CreditLimit
    , decimal AvailableBalance
);

public sealed record FfpMonthImportSnapshot
(
      int Year
    , int Month
    , DateTimeOffset ThroughAt
    , IReadOnlyList<ExternalFfpTransactionSnapshot> Transactions
    , IReadOnlyList<ExternalFfpAccountSnapshot> Accounts
);

public sealed record FfpMonthImportResult
(
      int Year
    , int Month
    , DateTimeOffset ThroughAt
    , int ImportedTransactions
    , int ImportedAccounts
    , decimal NetImportedScanValue
);

public interface IFfpMonthImportSource
{
    Task<FfpMonthImportSnapshot> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}

public interface IFfpMonthImportService
{
    Task<FfpMonthImportResult> ImportAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}
