namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Publishes service-originated financial events directly to FFP. This deliberately does not depend
/// on a POS being online: Additional Credit, Stipends and Monthly Allocation are management/service
/// responsibilities. OperationId/TransmissionID keeps retries idempotent on the FFP side.
/// </summary>
public sealed class ServiceFfpPublisherWorker(
    ServiceEventStore events,
    IExternalAccountingGateway accounting,
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
            catch (Exception ex)
            {
                logger.LogError(ex, "Service-originated FFP publication cycle failed; it will retry");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        var pending = await events.GetPendingFfpPublicationAsync(50, cancellationToken);
        var count = 0;
        foreach (CafeteriaServiceEventEnvelope envelope in pending)
        {
            try
            {
                DateTimeOffset? latestResetAt = await events.GetLatestDevelopmentResetAtAsync(cancellationToken);

                if (latestResetAt is DateTimeOffset resetAt && envelope.OccurredAt <= resetAt && envelope.Kind != CafeteriaServiceEventKind.DevelopmentReset)
                {
                    count++;
                    continue;
                }

                ExternalLedgerEntry? entry = await BuildLedgerEntryAsync(envelope, cancellationToken);
                if (entry is not null) await accounting.PublishLedgerEntryAsync(entry, cancellationToken);
                await events.RecordFfpAttemptAsync(envelope.OperationId, null, true, cancellationToken);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await events.RecordFfpAttemptAsync(envelope.OperationId, ex.Message, false, cancellationToken);
                logger.LogError(ex, "FFP publication failed for service event {OperationId} ({Kind}); later cycles will retry", envelope.OperationId, envelope.Kind);
                break;
            }
        }
        return count;
    }

    private async Task<ExternalLedgerEntry?> BuildLedgerEntryAsync(CafeteriaServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        return envelope.Kind switch
        {
            CafeteriaServiceEventKind.AdditionalCredit => await BuildAdditionalCreditAsync(envelope, cancellationToken),
            CafeteriaServiceEventKind.Stipend => await BuildStipendAsync(envelope, cancellationToken),
            CafeteriaServiceEventKind.MonthlyAllocation => await BuildMonthlyAllocationAsync(envelope, cancellationToken),
            CafeteriaServiceEventKind.MonthlyAllocationRecalculation => await BuildMonthlyAllocationRecalculationAsync(envelope, cancellationToken),
            CafeteriaServiceEventKind.DevelopmentReset => null,
            CafeteriaServiceEventKind.ProductCatalogImport => null,
            CafeteriaServiceEventKind.LegacyTransactionImport => null,
            _ => throw new InvalidOperationException($"Unsupported service-originated FFP event kind {envelope.Kind}.")
        };
    }

    private async Task<ExternalLedgerEntry> BuildAdditionalCreditAsync(CafeteriaServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var x = JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Additional Credit service event payload.");
        var account = await GetAccountAsync(x.CustomerId, cancellationToken);
        var creditLimit = account.CreditLimit + x.Amount;
        var available = account.AvailableBalance + x.Amount;
        var description = $"Additional Credit {x.Amount:N2}" + (string.IsNullOrWhiteSpace(x.Reason) ? string.Empty : $" - {x.Reason}");
        return new ExternalLedgerEntry(x.OperationId, x.CustomerId, ExternalLedgerEntryKind.AdditionalCredit, x.ApprovedAt,
            description, x.Amount, creditLimit, available, "ADDITIONAL CREDIT", x.ApprovedBy, "Additional Credit");
    }

    private async Task<ExternalLedgerEntry?> BuildStipendAsync(CafeteriaServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var x = JsonSerializer.Deserialize<StipendServiceEvent>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Stipend service event payload.");
        if (x.CardReductionAmount <= 0m) return null; // Cash-only stipend has no FFP cafeteria balance effect.
        var account = await GetAccountAsync(x.CustomerId, cancellationToken);
        var available = Math.Max(0m, account.AvailableBalance - x.CardReductionAmount);
        var description = $"Stipend {x.PaymentMode} - Cash: {x.CashAmount:N2}; Cafeteria Reduction: {x.CardReductionAmount:N2}"
            + (string.IsNullOrWhiteSpace(x.Notes) ? string.Empty : $" - {x.Notes}");
        return new ExternalLedgerEntry(x.OperationId, x.CustomerId, ExternalLedgerEntryKind.StipendCardReduction, x.ProcessedAt,
            description, x.CardReductionAmount, account.CreditLimit, available, "STIPEND", x.ProcessedBy, "Stipend Card Reduction");
    }

    private Task<ExternalLedgerEntry> BuildMonthlyAllocationAsync(CafeteriaServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var x = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid Monthly Allocation service event payload.");
        var allocation = new MonthlyCreditAllocation(x.CustomerId, x.Year, x.Month, x.EligibleWorkingDays,
            x.IsEligibleForCredit, x.CreditLimitPrice, x.SubsidyCost, x.CalculatedAt);
        var employeeName = string.IsNullOrWhiteSpace(x.EmployeeName) ? $"Employee #{x.CustomerId}" : x.EmployeeName.Trim();
        return Task.FromResult(new ExternalLedgerEntry(x.OperationId, x.CustomerId, ExternalLedgerEntryKind.MonthlyAllocation, x.CalculatedAt,
            $"{x.EligibleWorkingDays} new Tickets Assigned to {employeeName} By SYSTEM", allocation.TotalCreditLimit,
            allocation.TotalCreditLimit, allocation.TotalCreditLimit, "ALLOCATION", "SYSTEM", "Tickets Assigned"));
    }

    private Task<ExternalLedgerEntry> BuildMonthlyAllocationRecalculationAsync
    (
          CafeteriaServiceEventEnvelope envelope
        , CancellationToken cancellationToken
    )
    {
        MonthlyAllocationRecalculationServiceEvent recalculation = JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>
        (
            envelope.PayloadJson
        )
            ?? throw new InvalidOperationException("Invalid Monthly Allocation Recalculation service event payload.");

        MonthlyCreditAllocation previous = new
        (
              recalculation.PreviousAllocation.CustomerId
            , recalculation.PreviousAllocation.Year
            , recalculation.PreviousAllocation.Month
            , recalculation.PreviousAllocation.EligibleWorkingDays
            , recalculation.PreviousAllocation.IsEligibleForCredit
            , recalculation.PreviousAllocation.CreditLimitPrice
            , recalculation.PreviousAllocation.SubsidyCost
            , recalculation.PreviousAllocation.CalculatedAt
        );

        MonthlyCreditAllocation replacement = new
        (
              recalculation.ReplacementAllocation.CustomerId
            , recalculation.ReplacementAllocation.Year
            , recalculation.ReplacementAllocation.Month
            , recalculation.ReplacementAllocation.EligibleWorkingDays
            , recalculation.ReplacementAllocation.IsEligibleForCredit
            , recalculation.ReplacementAllocation.CreditLimitPrice
            , recalculation.ReplacementAllocation.SubsidyCost
            , recalculation.ReplacementAllocation.CalculatedAt
        );

        decimal delta = replacement.TotalCreditLimit - previous.TotalCreditLimit;
        string description = $"Monthly allocation recalculated for {replacement.Year:D4}-{replacement.Month:D2}: "
            + $"{previous.TotalCreditLimit:N2} -> {replacement.TotalCreditLimit:N2}; "
            + $"{replacement.EligibleWorkingDays} working day(s); "
            + $"credit rate {replacement.CreditLimitPrice:N2}; subsidy rate {replacement.SubsidyCost:N2}.";

        ExternalLedgerEntry entry = new
        (
              recalculation.OperationId
            , replacement.CustomerId
            , ExternalLedgerEntryKind.Adjustment
            , recalculation.RecalculatedAt
            , description
            , delta
            , replacement.TotalCreditLimit
            , replacement.TotalCreditLimit
            , "ALLOCATION RECALC"
            , recalculation.RecalculatedBy
            , "Monthly Allocation Recalculation"
        );

        return Task.FromResult(entry);
    }

    private async Task<ExternalAccountSummary> GetAccountAsync(int customerId, CancellationToken cancellationToken)
    {
        var accounts = await accounting.GetAccountSummariesAsync(cancellationToken);
        return accounts.SingleOrDefault(x => x.CustomerId == customerId)
            ?? throw new InvalidOperationException($"FFP account summary was not found for employee {customerId}.");
    }
}
