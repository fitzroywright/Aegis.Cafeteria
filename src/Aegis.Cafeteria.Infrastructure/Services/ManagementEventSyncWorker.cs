namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Pulls immutable management/month-opening events from Aegis.Cafeteria.Services and commits them to the POS local ledger.
/// The service is never required to complete a sale; synchronization catches up after outages.
/// </summary>
public sealed class ManagementEventSyncWorker(
    CafeteriaServicesClient client,
    CafeteriaServicesOptions options,
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    ICafeteriaPersistenceStore persistence,
    ICustomerAccountQuery accountQuery,
    ILogger<ManagementEventSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PullAndApplyAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Cafeteria Services event synchronization failed; POS will retry"); }
            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
        }
    }

    internal async Task<int> PullAndApplyAsync(CancellationToken cancellationToken)
    {
        await using var checkpointDb = await factory.CreateDbContextAsync(cancellationToken);
        var last = await checkpointDb.ManagementEventInbox
            .Where(x => x.ProcessedAt != null)
            .Select(x => (long?)x.Sequence)
            .MaxAsync(cancellationToken) ?? 0L;

        var events = await client.GetEventsAsync(last, 100, cancellationToken);
        var processed = 0;
        foreach (var e in events.OrderBy(x => x.Sequence))
        {
            if (await IsProcessedAsync(e.OperationId, cancellationToken)) continue;
            await SaveInboxAsync(e, cancellationToken);
            try
            {
                await ApplyAsync(e, cancellationToken);
                await MarkProcessedAsync(e.OperationId, null, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await MarkProcessedAsync(e.OperationId, ex.Message, cancellationToken);
                throw;
            }
        }
        return processed;
    }

    private async Task ApplyAsync(CafeteriaServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Kind)
        {
            case CafeteriaServiceEventKind.AdditionalCredit:
            {
                var x = JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(envelope.PayloadJson) ?? throw new InvalidOperationException("Invalid Additional Credit event payload.");
                await UpdateAdditionalCreditLimitAsync(x.CustomerId, x.AdditionalCreditLimit, cancellationToken);
                var approval = new AdditionalCreditApproval(x.OperationId, x.CustomerId, x.Amount, x.ApprovedBy, x.ApprovedAt, x.Reason);
                await persistence.SaveAdditionalCreditApprovalAsync(approval, cancellationToken);
                _ = await accountQuery.GetCurrentAsync(x.CustomerId, cancellationToken) ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");
                break;
            }
            case CafeteriaServiceEventKind.Stipend:
            {
                var x = JsonSerializer.Deserialize<StipendServiceEvent>(envelope.PayloadJson) ?? throw new InvalidOperationException("Invalid Stipend event payload.");
                var stipend = new Stipend(x.OperationId, x.CustomerId, x.CashAmount, x.CardReductionAmount, x.PaymentMode, x.ProcessedBy, x.ProcessedAt, x.ExpenseReportReference, x.Notes);
                await persistence.SaveStipendAsync(stipend, cancellationToken);
                _ = await accountQuery.GetCurrentAsync(x.CustomerId, cancellationToken) ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");
                break;
            }
            case CafeteriaServiceEventKind.MonthlyAllocation:
            {
                var x = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(envelope.PayloadJson) ?? throw new InvalidOperationException("Invalid Monthly Allocation event payload.");
                var allocation = new MonthlyCreditAllocation(x.CustomerId, x.Year, x.Month, x.EligibleWorkingDays, x.IsEligibleForCredit, x.CreditLimitPrice, x.SubsidyCost, x.CalculatedAt);
                await persistence.SaveMonthlyCreditAllocationAsync(allocation, cancellationToken);
                // Services owns FFP publication for service-originated month-opening events; the POS only
                // materializes the immutable allocation into its local account/history projection.
                _ = await accountQuery.GetAsync(x.CustomerId, x.Year, x.Month, cancellationToken);
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported Cafeteria Services event kind {envelope.Kind}.");
        }
    }


    private async Task UpdateAdditionalCreditLimitAsync(int customerId, decimal limit, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Employee is not available in the local Cafeteria cache.");
        if (customer.AdditionalCreditLimit == limit) return;
        customer.AdditionalCreditLimit = limit;
        customer.Revision = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsProcessedAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ManagementEventInbox.AnyAsync(x => x.OperationId == operationId && x.ProcessedAt != null, cancellationToken);
    }

    private async Task SaveInboxAsync(CafeteriaServiceEventEnvelope e, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.ManagementEventInbox.SingleOrDefaultAsync(x => x.OperationId == e.OperationId, cancellationToken);
        if (row is null)
        {
            db.ManagementEventInbox.Add(new ManagementEventInboxRow
            {
                OperationId=e.OperationId, Sequence=e.Sequence, Kind=(int)e.Kind, CustomerId=e.CustomerId,
                OccurredAt=e.OccurredAt, PayloadJson=e.PayloadJson, ReceivedAt=DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task MarkProcessedAsync(Guid operationId, string? error, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.ManagementEventInbox.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        row.LastError = error;
        row.ProcessedAt = error is null ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(cancellationToken);
    }
}
