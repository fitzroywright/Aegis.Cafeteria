namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Publishes service-originated Cafeteria transactions through the FFP Integration Plane.
/// FFP Manager is a projection target only; Cafeteria never reads balances back from it.
/// OperationId is the idempotency key across retries.
/// </summary>
public sealed class ServiceFfpPublisherWorker(
    ServiceEventStore events,
    IEmployeeIdScanPublisher publisher,
    ExternalPublicationOptions options,
    ILogger<ServiceFfpPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Integration-plane publication cycle failed; it will retry");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(1, options.RetryIntervalSeconds)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CafeteriaServiceEventEnvelope> pending = await events.GetPendingFfpPublicationAsync(
            Math.Clamp(options.BatchSize, 1, 500),
            Math.Max(1, options.PoisonMessageAttemptLimit),
            cancellationToken);

        int count = 0;
        foreach (CafeteriaServiceEventEnvelope envelope in pending)
        {
            try
            {
                DateTimeOffset? latestResetAt = await events.GetLatestDevelopmentResetAtAsync(cancellationToken);

                if (latestResetAt is DateTimeOffset resetAt
                    && envelope.OccurredAt <= resetAt
                    && envelope.Kind != CafeteriaServiceEventKind.DevelopmentReset)
                {
                    await events.RecordFfpAttemptAsync(
                        envelope.OperationId,
                        null,
                        true,
                        cancellationToken);

                    count++;
                    continue;
                }

                CafeteriaOutboundTransaction? transaction = BuildTransaction(envelope);
                if (transaction is not null)
                    await publisher.PublishAsync(transaction, cancellationToken);

                await events.RecordFfpAttemptAsync(
                    envelope.OperationId,
                    null,
                    true,
                    cancellationToken);

                count++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await events.RecordFfpAttemptAsync(
                    envelope.OperationId,
                    exception.Message,
                    false,
                    cancellationToken);

                logger.LogError(
                    exception,
                    "Integration-plane publication failed for service event {OperationId} ({Kind}); later cycles will retry",
                    envelope.OperationId,
                    envelope.Kind);

                break;
            }
        }

        return count;
    }

    private static CafeteriaOutboundTransaction? BuildTransaction(
        CafeteriaServiceEventEnvelope envelope)
    {
        return envelope.Kind switch
        {
            CafeteriaServiceEventKind.AdditionalCredit => BuildAdditionalCredit(envelope),
            CafeteriaServiceEventKind.Stipend => BuildStipend(envelope),
            CafeteriaServiceEventKind.MonthlyAllocation => BuildMonthlyAllocation(envelope),
            CafeteriaServiceEventKind.MonthlyAllocationRecalculation => BuildMonthlyAllocationRecalculation(envelope),
            CafeteriaServiceEventKind.AccountPeriodClose => null,
            CafeteriaServiceEventKind.DevelopmentReset => null,
            CafeteriaServiceEventKind.ProductCatalogImport => null,
            CafeteriaServiceEventKind.LegacyTransactionImport => null,
            _ => throw new InvalidOperationException(
                $"Unsupported outbound Cafeteria event kind {envelope.Kind}.")
        };
    }

    private static CafeteriaOutboundTransaction BuildAdditionalCredit(
        CafeteriaServiceEventEnvelope envelope)
    {
        AdditionalCreditServiceEvent value = JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(
            envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Additional Credit service event payload.");

        string description = $"Additional Credit {value.Amount:N2}"
            + (string.IsNullOrWhiteSpace(value.Reason)
                ? string.Empty
                : $" - {value.Reason}");

        return new CafeteriaOutboundTransaction(
            value.OperationId,
            value.CustomerId,
            "AdditionalCredit",
            value.ApprovedAt,
            value.Amount,
            description,
            "Management",
            value.ApprovedBy);
    }

    private static CafeteriaOutboundTransaction? BuildStipend(
        CafeteriaServiceEventEnvelope envelope)
    {
        StipendServiceEvent value = JsonSerializer.Deserialize<StipendServiceEvent>(
            envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Stipend service event payload.");

        if (value.CardReductionAmount <= 0m)
            return null;

        string description =
            $"Stipend {value.PaymentMode} - Cash: {value.CashAmount:N2}; Cafeteria Reduction: {value.CardReductionAmount:N2}"
            + (string.IsNullOrWhiteSpace(value.Notes)
                ? string.Empty
                : $" - {value.Notes}");

        return new CafeteriaOutboundTransaction(
            value.OperationId,
            value.CustomerId,
            "StipendCardReduction",
            value.ProcessedAt,
            value.CardReductionAmount,
            description,
            "Management",
            value.ProcessedBy);
    }

    private static CafeteriaOutboundTransaction BuildMonthlyAllocation(
        CafeteriaServiceEventEnvelope envelope)
    {
        MonthlyAllocationServiceEvent value = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(
            envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Monthly Allocation service event payload.");

        decimal amount = value.EligibleWorkingDays * value.CreditLimitPrice;
        string employeeName = string.IsNullOrWhiteSpace(value.EmployeeName)
            ? $"Employee #{value.CustomerId}"
            : value.EmployeeName.Trim();

        return new CafeteriaOutboundTransaction(
            value.OperationId,
            value.CustomerId,
            "MonthlyAllocation",
            value.CalculatedAt,
            amount,
            $"{value.EligibleWorkingDays} new Tickets Assigned to {employeeName} By SYSTEM",
            "Allocation",
            "SYSTEM");
    }

    private static CafeteriaOutboundTransaction BuildMonthlyAllocationRecalculation(
        CafeteriaServiceEventEnvelope envelope)
    {
        MonthlyAllocationRecalculationServiceEvent value =
            JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>(
                envelope.PayloadJson)
            ?? throw new InvalidOperationException(
                "Invalid Monthly Allocation Recalculation service event payload.");

        decimal previous =
            value.PreviousAllocation.EligibleWorkingDays
            * value.PreviousAllocation.CreditLimitPrice;

        decimal replacement =
            value.ReplacementAllocation.EligibleWorkingDays
            * value.ReplacementAllocation.CreditLimitPrice;

        return new CafeteriaOutboundTransaction(
            value.OperationId,
            value.ReplacementAllocation.CustomerId,
            "MonthlyAllocationRecalculation",
            value.RecalculatedAt,
            replacement - previous,
            $"Monthly allocation explicitly recalculated for {value.ReplacementAllocation.Year:D4}-{value.ReplacementAllocation.Month:D2}",
            "Management",
            value.RecalculatedBy);
    }
}
