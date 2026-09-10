namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Domain.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class AccountPeriodCloseServiceEventQuery(IDbContextFactory<ServiceDbContext> factory)
{
    public async Task<IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent>> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);

        List<string> payloads = await database.Events
            .AsNoTracking()
            .Where(serviceEvent => serviceEvent.Kind == (int)CafeteriaServiceEventKind.AccountPeriodClose)
            .OrderBy(serviceEvent => serviceEvent.Sequence)
            .Select(serviceEvent => serviceEvent.PayloadJson)
            .ToListAsync(cancellationToken);

        Dictionary<int, AccountPeriodCloseServiceEvent> result = [];
        foreach (string payloadJson in payloads)
        {
            AccountPeriodCloseServiceEvent? close = JsonSerializer.Deserialize<AccountPeriodCloseServiceEvent>(payloadJson);
            if (close is not null && close.Year == year && close.Month == month)
            {
                result[close.CustomerId] = close;
            }
        }

        return result;
    }

    public async Task<bool> IsClosedAsync
    (
          int customerId
        , int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        IReadOnlyDictionary<int, AccountPeriodCloseServiceEvent> closes = await GetMonthAsync(year, month, cancellationToken);
        return closes.ContainsKey(customerId);
    }
}
