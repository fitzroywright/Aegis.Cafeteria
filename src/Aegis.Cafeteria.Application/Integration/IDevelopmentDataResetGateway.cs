namespace Aegis.Cafeteria.Application.Integration;

public interface IDevelopmentDataResetGateway
{
    Task<DevelopmentDataResetResult> ResetAsync
    (
          string resetBy
        , DateTimeOffset resetAt
        , CancellationToken cancellationToken = default
    );
}

public sealed record DevelopmentDataResetResult
(
      int DeletedLedgerEntries
    , int ResetEmployeeAccounts
);
