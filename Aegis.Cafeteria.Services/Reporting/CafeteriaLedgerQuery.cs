namespace Aegis.Cafeteria.Services.Reporting;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed record CafeteriaLedgerLine(
    Guid OperationId,
    int CustomerId,
    ExternalLedgerEntryKind Kind,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string Description,
    string UserName);

public sealed class CafeteriaLedgerQuery(
    IDbContextFactory<ServiceDbContext> factory)
{
    public async Task<IReadOnlyList<CafeteriaLedgerLine>> GetAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext database =
            await factory.CreateDbContextAsync(cancellationToken);

        int[] supportedKinds =
        [
              (int)CafeteriaServiceEventKind.ExternalLedgerEntry
            , (int)CafeteriaServiceEventKind.AdditionalCredit
            , (int)CafeteriaServiceEventKind.Stipend
            , (int)CafeteriaServiceEventKind.MonthlyAllocation
            , (int)CafeteriaServiceEventKind.MonthlyAllocationRecalculation
        ];

        // SQLite cannot translate DateTimeOffset range comparisons. Stream
        // supported events in sequence order and compare instants in .NET.
        IAsyncEnumerable<ServiceEventRow> rows = database.Events
            .AsNoTracking()
            .Where(row => supportedKinds.Contains(row.Kind))
            .OrderBy(row => row.Sequence)
            .AsAsyncEnumerable();

        List<CafeteriaLedgerLine> result = [];

        await foreach (ServiceEventRow row in rows.WithCancellation(cancellationToken))
        {
            if (row.OccurredAt < from || row.OccurredAt >= to)
                continue;

            CafeteriaLedgerLine? line = ToLedgerLine(row);
            if (line is not null)
                result.Add(line);
        }

        return result;
    }

    public async Task<IReadOnlyList<CafeteriaLedgerLine>> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset from = new(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        return await GetAsync(
            from,
            from.AddMonths(1),
            cancellationToken);
    }

    private static CafeteriaLedgerLine? ToLedgerLine(ServiceEventRow row)
    {
        CafeteriaServiceEventKind kind = (CafeteriaServiceEventKind)row.Kind;

        return kind switch
        {
            CafeteriaServiceEventKind.ExternalLedgerEntry =>
                FromExternalLedger(row),

            CafeteriaServiceEventKind.AdditionalCredit =>
                FromAdditionalCredit(row),

            CafeteriaServiceEventKind.Stipend =>
                FromStipend(row),

            CafeteriaServiceEventKind.MonthlyAllocation =>
                FromMonthlyAllocation(row),

            CafeteriaServiceEventKind.MonthlyAllocationRecalculation =>
                FromMonthlyAllocationRecalculation(row),

            _ => null
        };
    }

    private static CafeteriaLedgerLine FromExternalLedger(ServiceEventRow row)
    {
        ExternalLedgerEntry value =
            JsonSerializer.Deserialize<ExternalLedgerEntry>(row.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Invalid Cafeteria ledger payload for operation {row.OperationId}.");

        return new CafeteriaLedgerLine(
            value.OperationId,
            value.CustomerId,
            value.Kind,
            value.OccurredAt,
            value.ScanValue,
            value.Description,
            value.UserName);
    }

    private static CafeteriaLedgerLine FromAdditionalCredit(ServiceEventRow row)
    {
        AdditionalCreditServiceEvent value =
            JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(row.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Invalid Additional Credit payload for operation {row.OperationId}.");

        return new CafeteriaLedgerLine(
            value.OperationId,
            value.CustomerId,
            ExternalLedgerEntryKind.AdditionalCredit,
            value.ApprovedAt,
            value.Amount,
            string.IsNullOrWhiteSpace(value.Reason)
                ? "Additional Credit"
                : $"Additional Credit - {value.Reason}",
            value.ApprovedBy);
    }

    private static CafeteriaLedgerLine? FromStipend(ServiceEventRow row)
    {
        StipendServiceEvent value =
            JsonSerializer.Deserialize<StipendServiceEvent>(row.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Invalid Stipend payload for operation {row.OperationId}.");

        if (value.Amount <= 0m)
            return null;

        return new CafeteriaLedgerLine(
            value.OperationId,
            value.CustomerId,
            ExternalLedgerEntryKind.StipendCardReduction,
            value.ProcessedAt,
            value.Amount,
            string.IsNullOrWhiteSpace(value.Reason) ? "Stipend Deduction" : "Stipend Deduction - " + value.Reason,
            value.ProcessedBy);
    }

    private static CafeteriaLedgerLine FromMonthlyAllocation(ServiceEventRow row)
    {
        MonthlyAllocationServiceEvent value =
            JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(row.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Invalid Monthly Allocation payload for operation {row.OperationId}.");

        decimal amount =
            value.EligibleWorkingDays * value.CreditLimitPrice;

        return new CafeteriaLedgerLine(
            value.OperationId,
            value.CustomerId,
            ExternalLedgerEntryKind.MonthlyAllocation,
            value.CalculatedAt,
            amount,
            "Monthly Allocation",
            "SYSTEM");
    }

    private static CafeteriaLedgerLine FromMonthlyAllocationRecalculation(
        ServiceEventRow row)
    {
        MonthlyAllocationRecalculationServiceEvent value =
            JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>(
                row.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Invalid Monthly Allocation Recalculation payload for operation {row.OperationId}.");

        decimal previous =
            value.PreviousAllocation.EligibleWorkingDays
            * value.PreviousAllocation.CreditLimitPrice;

        decimal replacement =
            value.ReplacementAllocation.EligibleWorkingDays
            * value.ReplacementAllocation.CreditLimitPrice;

        return new CafeteriaLedgerLine(
            value.OperationId,
            value.ReplacementAllocation.CustomerId,
            ExternalLedgerEntryKind.Adjustment,
            value.RecalculatedAt,
            replacement - previous,
            "Monthly Allocation Recalculation",
            value.RecalculatedBy);
    }
}
