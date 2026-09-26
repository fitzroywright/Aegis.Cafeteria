namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Publishes service-originated Cafeteria transactions through the configured integration plane.
/// The external system is a projection target only; Cafeteria never reads balances back from it.
/// OperationId is the idempotency key across retries.
/// </summary>
public sealed class ExternalPublicationWorker(
    ServiceEventStore events,
    IEmployeeIdScanPublisher publisher,
    ExternalPublicationOptions options,
    ILogger<ExternalPublicationWorker> logger) : BackgroundService
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
        IReadOnlyList<CafeteriaServiceEventEnvelope> pending = await events.GetPendingExternalPublicationAsync(
            Math.Clamp(options.BatchSize, 1, 500),
            Math.Max(1, options.PoisonMessageAttemptLimit),
            cancellationToken);

        int count = 0;
        foreach (CafeteriaServiceEventEnvelope envelope in pending)
        {
            try
            {
                CafeteriaOutboundTransaction? transaction = BuildTransaction(envelope);
                if (transaction is not null)
                    await publisher.PublishAsync(transaction, cancellationToken);

                await events.RecordPublicationAttemptAsync(
                    envelope.OperationId,
                    null,
                    true,
                    cancellationToken);

                count++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await events.RecordPublicationAttemptAsync(
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
            CafeteriaServiceEventKind.ProductCatalogImport => null,
            CafeteriaServiceEventKind.LegacyTransactionImport => null,
            CafeteriaServiceEventKind.PosTransaction => BuildPosTransaction(envelope),
            CafeteriaServiceEventKind.ExternalLedgerEntry => BuildExternalLedgerEntry(envelope),
            _ => throw new InvalidOperationException(
                $"Unsupported outbound Cafeteria event kind {envelope.Kind}.")
        };
    }

    private static CafeteriaOutboundTransaction BuildPosTransaction(
        CafeteriaServiceEventEnvelope envelope)
    {
        PosTransactionEvent value = JsonSerializer.Deserialize<PosTransactionEvent>(
            envelope.PayloadJson)
            ?? throw new InvalidOperationException("Invalid POS transaction service event payload.");

        using JsonDocument document = JsonDocument.Parse(value.PayloadJson);
        JsonElement root = document.RootElement;

        decimal amount = 0m;
        if (root.TryGetProperty("Total", out JsonElement totalElement) && totalElement.TryGetDecimal(out decimal total))
            amount = total;

        string itemList = BuildPosItemList(root);
        string description = value.Kind switch
        {
            "SaleCompleted" => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria Purchase {amount:N2}"
                : itemList,
            "CreditSaleCompleted" => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria Credit Purchase {amount:N2}"
                : itemList,
            "RefundIssuedFull" => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria Full Refund {amount:N2}"
                : $"Refund: {itemList}",
            "RefundIssuedPartial" => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria Partial Refund {amount:N2}"
                : $"Refund: {itemList}",
            "SaleVoided" => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria Sale Voided {amount:N2}"
                : $"Void: {itemList}",
            _ => string.IsNullOrWhiteSpace(itemList)
                ? $"Cafeteria POS {value.Kind} {amount:N2}"
                : itemList
        };

        string identificationMethod = "POS";
        string? identificationCode = null;

        return new CafeteriaOutboundTransaction(
            value.EventId,
            value.CustomerId,
            value.Kind switch
            {
                "SaleCompleted" => "Purchase",
                "CreditSaleCompleted" => "Purchase",
                "RefundIssuedFull" => "Refund",
                "RefundIssuedPartial" => "Refund",
                "SaleVoided" => "Refund",
                _ => "Purchase"
            },
            value.OccurredAt,
            amount,
            description,
            identificationMethod,
            value.Actor,
            identificationCode);
    }

    private static string BuildPosItemList(JsonElement root)
    {
        if (!root.TryGetProperty("Items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        List<string> descriptions = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            string name = item.TryGetProperty("ProductName", out JsonElement nameElement)
                ? nameElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            int quantity = 1;
            if (item.TryGetProperty("Quantity", out JsonElement quantityElement)
                && quantityElement.TryGetInt32(out int parsedQuantity)
                && parsedQuantity > 0)
            {
                quantity = parsedQuantity;
            }

            descriptions.Add(quantity == 1 ? name : $"{quantity} x {name}");
        }

        return string.Join(", ", descriptions);
    }

    private static CafeteriaOutboundTransaction BuildExternalLedgerEntry(
        CafeteriaServiceEventEnvelope envelope)
    {
        ExternalLedgerEntry value = JsonSerializer.Deserialize<ExternalLedgerEntry>(
            envelope.PayloadJson)
            ?? throw new InvalidOperationException(
                "Invalid Cafeteria ledger event payload.");

        return new CafeteriaOutboundTransaction(
            value.OperationId,
            value.CustomerId,
            value.Kind.ToString(),
            value.OccurredAt,
            value.ScanValue,
            value.Description,
            value.ScanCode,
            value.UserName,
            value.Code);
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
