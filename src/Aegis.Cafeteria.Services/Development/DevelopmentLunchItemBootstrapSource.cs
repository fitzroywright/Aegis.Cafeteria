namespace Aegis.Cafeteria.Services.Development;

using Aegis.Cafeteria.Application.Integration;

public sealed class DevelopmentLunchItemBootstrapSource : ILunchItemBootstrapSource
{
    public Task<IReadOnlyList<ExternalProductSnapshot>> GetLunchItemsAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ExternalProductSnapshot> items =
        [
              new ExternalProductSnapshot(1, "FFP00", "Cafeteria Policy", 800m, 600m)
            , new ExternalProductSnapshot(2, "LUNCH", "Lunch", 800m, 500m)
            , new ExternalProductSnapshot(3, "JUICE", "Juice", 200m, 150m)
        ];

        return Task.FromResult(items);
    }
}
