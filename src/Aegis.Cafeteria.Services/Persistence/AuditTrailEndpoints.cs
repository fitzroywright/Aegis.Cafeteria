namespace Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public static class AuditTrailEndpoints
{
    public static void Map(WebApplication application)
    {
        application.MapGet
        (
              "/api/management/audit"
            , async
            (
                  string? actor
                , string? action
                , int? customerId
                , bool? succeeded
                , DateTimeOffset? from
                , DateTimeOffset? to
                , int? take
                , IDbContextFactory<ServiceDbContext> factory
                , CancellationToken cancellationToken
            ) =>
            {
                int limit = Math.Clamp(take ?? 100, 1, 500);

                await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
                AuditTrailRow[] candidates = await database.AuditTrail
                    .AsNoTracking()
                    .OrderByDescending(row => row.Id)
                    .Take(2000)
                    .ToArrayAsync(cancellationToken);

                IEnumerable<AuditTrailRow> query = candidates;

                if (!string.IsNullOrWhiteSpace(actor))
                {
                    query = query.Where(row => row.Actor.Contains(actor.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(action))
                {
                    query = query.Where(row => row.Action.Contains(action.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (customerId.HasValue)
                {
                    query = query.Where(row => row.CustomerId == customerId.Value);
                }

                if (succeeded.HasValue)
                {
                    query = query.Where(row => row.Succeeded == succeeded.Value);
                }

                if (from.HasValue)
                {
                    query = query.Where(row => row.OccurredAt >= from.Value);
                }

                if (to.HasValue)
                {
                    query = query.Where(row => row.OccurredAt <= to.Value);
                }

                return Results.Ok(query.Take(limit).ToArray());
            }
        ).RequireAuthorization("CafeteriaManagement");
    }
}
