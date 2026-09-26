namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public sealed class PosTransactionOutboxWorker(
    LocalPersistenceStore store,
    CafeteriaServicesClient servicesClient,
    ILogger<PosTransactionOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "POS transaction outbox cycle failed; queued events will retry");

                // Never spin in a hot exception loop. A failed outbox probe must
                // yield before retrying so it cannot starve the WinForms UI or
                // flood the debugger/output pipeline.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch step = Stopwatch.StartNew();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<PosTransactionOutboxRow> pending = await store.GetPendingPosTransactionsAsync(50, now, cancellationToken);
        logger.LogInformation("PERF Outbox pending query {ElapsedMs} ms Count={Count}", step.ElapsedMilliseconds, pending.Count);
        int published = 0;

        foreach (PosTransactionOutboxRow row in pending)
        {
            try
            {
                PosTransactionEvent transactionEvent = new(row.EventId,row.CorrelationId,row.Kind,row.OrderId,row.CustomerId,row.Actor,row.TerminalId,row.AccountYear,row.AccountMonth,row.OccurredAt,row.PayloadJson,row.Reason);
                step.Restart();
                await servicesClient.PublishTransactionEventAsync(transactionEvent, cancellationToken);
                logger.LogInformation("PERF Outbox EventId={EventId} HTTP publish {ElapsedMs} ms", row.EventId, step.ElapsedMilliseconds);
                step.Restart();
                await store.MarkPosTransactionPublishedAsync(row.EventId, DateTimeOffset.UtcNow, cancellationToken);
                logger.LogInformation("PERF Outbox EventId={EventId} mark published {ElapsedMs} ms", row.EventId, step.ElapsedMilliseconds);
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DateTimeOffset attemptedAt = DateTimeOffset.UtcNow;
                int nextAttemptNumber = row.AttemptCount + 1;
                double delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(nextAttemptNumber, 8)));
                DateTimeOffset nextAttemptAt = attemptedAt.AddSeconds(delaySeconds);
                await store.RecordPosTransactionFailureAsync(row.EventId, exception.Message, attemptedAt, nextAttemptAt, cancellationToken);
                logger.LogWarning(exception, "POS transaction event {EventId} could not be published; retry scheduled for {NextAttemptAt}", row.EventId, nextAttemptAt);
                break;
            }
        }

        logger.LogInformation("PERF Outbox batch TOTAL {ElapsedMs} ms Published={Published} Pending={Pending}", total.ElapsedMilliseconds, published, pending.Count);
        return published;
    }
}
