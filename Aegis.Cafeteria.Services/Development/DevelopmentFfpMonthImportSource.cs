namespace Aegis.Cafeteria.Services.Development;

using Aegis.Cafeteria.Application.Integration;

public sealed class DevelopmentFfpMonthImportSource : IMonthImportSource
{
    public Task<MonthImportSnapshot> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset throughAt = DateTimeOffset.Now;
        MonthImportSnapshot result = new
        (
              year
            , month
            , throughAt
            , []
            , []
        );
        return Task.FromResult(result);
    }
}
