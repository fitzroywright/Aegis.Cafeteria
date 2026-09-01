namespace Aegis.Cafeteria.Services.Development;

using Aegis.Cafeteria.Application.Integration;

public sealed class DevelopmentFfpMonthImportSource : IFfpMonthImportSource
{
    public Task<FfpMonthImportSnapshot> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset throughAt = DateTimeOffset.Now;
        FfpMonthImportSnapshot result = new
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
