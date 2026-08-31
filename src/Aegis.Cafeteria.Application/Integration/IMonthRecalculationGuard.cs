namespace Aegis.Cafeteria.Application.Integration;

public interface IMonthRecalculationGuard
{
    Task<MonthRecalculationGuardResult> CheckAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    );
}

public sealed record MonthRecalculationGuardResult
(
      bool IsSafe
    , int ActivityCount
    , string? Reason
);
