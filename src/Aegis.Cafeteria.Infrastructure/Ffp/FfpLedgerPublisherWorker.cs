namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Publishes the durable FFP compatibility outbox. Checkout never waits for FFP network/database availability.
/// TransmissionID makes retries safe on the FFP side.
/// </summary>
public sealed class FfpLedgerPublisherWorker(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    IExternalAccountingGateway gateway,
    ICustomerAccountQuery accountQuery,
    CafeteriaPersistenceOptions persistence,
    CafeteriaServicesOptions servicesOptions,
    ILogger<FfpLedgerPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        await RecoverMissingProjectionRowsAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "FFP ledger outbox publication cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.FfpLedgerOutbox
            .Where(x => x.State == FfpOutboxState.Pending || x.State == FfpOutboxState.Failed)
            .ToListAsync(cancellationToken);
        // DateTimeOffset ordering is deliberately in-memory for SQLite compatibility.
        var rows = candidates.OrderBy(x => x.OccurredAt).ThenBy(x => x.OperationId).Take(persistence.FfpPublicationBatchSize).ToList();
        var count=0;
        foreach (var row in rows)
        {
            row.State=FfpOutboxState.Processing; row.AttemptCount++; row.LastAttemptAt=DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                var account = await accountQuery.GetCurrentAsync(row.EmployeeId, cancellationToken);
                var creditLimit = account?.FinancialStatus.TotalCreditLimit ?? row.CreditLimit;
                var available = account?.FinancialStatus.TotalAvailableToSpend ?? row.AvailableBalance;
                await gateway.PublishLedgerEntryAsync(new ExternalLedgerEntry(
                    row.OperationId,row.EmployeeId,(ExternalLedgerEntryKind)row.Kind,row.OccurredAt,row.Description,row.ScanValue,
                    creditLimit,available,row.ScanCode,row.UserName,row.IdName), cancellationToken);
                row.State=FfpOutboxState.Succeeded; row.LastError=null; count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.LastError=ex.Message;
                row.State=row.AttemptCount >= persistence.FfpPoisonMessageAttemptLimit ? FfpOutboxState.Poisoned : FfpOutboxState.Failed;
                logger.LogError(ex,"FFP ledger operation {OperationId} failed on attempt {AttemptCount}; state {State}",row.OperationId,row.AttemptCount,row.State);
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return count;
    }


    private async Task RecoverMissingProjectionRowsAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = (await db.FfpLedgerOutbox.AsNoTracking().Select(x => x.OperationId).ToListAsync(cancellationToken)).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<MonthlyCreditAllocationRow> allocations = servicesOptions.Enabled && servicesOptions.UseServiceMonthlyAllocations
            ? Array.Empty<MonthlyCreditAllocationRow>()
            : await db.MonthlyCreditAllocations.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var a in allocations)
        {
            var operationId = MonthlyOperationId(a.CustomerId, a.Year, a.Month);
            if (existing.Contains(operationId)) continue;
            db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
            {
                OperationId=operationId, EmployeeId=a.CustomerId, Kind=(int)ExternalLedgerEntryKind.MonthlyAllocation, OccurredAt=a.CalculatedAt,
                Description=$"{a.EligibleWorkingDays} new Tickets Assigned to Employee #{a.CustomerId} By SYSTEM", ScanValue=a.TotalCreditLimit, CreditLimit=a.TotalCreditLimit,
                AvailableBalance=a.TotalCreditLimit, ScanCode="ALLOCATION", UserName="SYSTEM", IdName="Tickets Assigned", State=FfpOutboxState.Pending, CreatedAt=now
            });
            existing.Add(operationId);
        }

        var completed = await db.Orders.AsNoTracking().Include(x=>x.Items).Include(x=>x.Payments)
            .Include(x=>x.Refunds).ThenInclude(x=>x.Items)
            .Include(x=>x.Refunds).ThenInclude(x=>x.Allocations)
            .Where(x=>x.Status==(int)Aegis.Cafeteria.Domain.Orders.OrderStatus.Completed).ToListAsync(cancellationToken);
        foreach (var order in completed)
        {
            var accountAmount=order.Payments.Where(x=>x.Method==(int)Aegis.Cafeteria.Domain.Orders.PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount);
            if(accountAmount>0m && !existing.Contains(order.Id))
            {
                db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
                {
                    OperationId=order.Id,EmployeeId=order.CustomerId,Kind=(int)ExternalLedgerEntryKind.Purchase,OccurredAt=order.CompletedAt??order.UpdatedAt,
                    Description=BuildRecoveredPurchaseDescription(order),ScanValue=accountAmount,CreditLimit=0m,AvailableBalance=0m,ScanCode="PURCHASE",
                    UserName=string.IsNullOrWhiteSpace(order.CashierUserId)?"SYSTEM":order.CashierUserId,IdName="Cafeteria Purchase",State=FfpOutboxState.Pending,CreatedAt=now
                });
                existing.Add(order.Id);
            }
            foreach(var refund in order.Refunds)
            {
                var refunded=refund.Allocations.Where(x=>x.Method==(int)Aegis.Cafeteria.Domain.Orders.PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount);
                if(refunded<=0m || existing.Contains(refund.Id)) continue;
                db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
                {
                    OperationId=refund.Id,EmployeeId=order.CustomerId,Kind=(int)ExternalLedgerEntryKind.Refund,OccurredAt=refund.RefundedAt,
                    Description=BuildRecoveredRefundDescription(order, refund, refunded),ScanValue=-refunded,CreditLimit=0m,AvailableBalance=0m,
                    ScanCode="REFUND",UserName=string.IsNullOrWhiteSpace(refund.RefundedBy)?"SYSTEM":refund.RefundedBy,IdName="Cafeteria Refund",State=FfpOutboxState.Pending,CreatedAt=now
                });
                existing.Add(refund.Id);
            }
        }
        if(db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Recovered missing FFP projection rows from durable Aegis transactions");
        }
    }

    private static string BuildRecoveredPurchaseDescription(Aegis.Cafeteria.Infrastructure.Persistence.Models.OrderRow order)
    {
        var items=string.Join("; ",order.Items.Select(x=>$"{x.ProductName} x{x.Quantity} @ {x.UnitPrice:N2}"));
        var total=order.Items.Sum(x=>x.UnitPrice*x.Quantity);
        var cafeteria=order.Payments.Where(x=>x.Method==(int)Aegis.Cafeteria.Domain.Orders.PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount);
        return cafeteria==total?$"{items} - Total: {total:N2}":$"{items} - Order Total: {total:N2}; Cafeteria: {cafeteria:N2}";
    }

    private static string BuildRecoveredRefundDescription(Aegis.Cafeteria.Infrastructure.Persistence.Models.OrderRow order, Aegis.Cafeteria.Infrastructure.Persistence.Models.OrderRefundRow refund, decimal cafeteriaAmount)
    {
        var names=order.Items.ToDictionary(x=>x.Id,x=>x.ProductName);
        var items=string.Join("; ",refund.Items.Select(x=>$"{names.GetValueOrDefault(x.OrderItemId,"Item")} x{x.Quantity} @ {x.UnitPrice:N2}"));
        return $"Refund - {items} - Cafeteria Refund: {cafeteriaAmount:N2}";
    }

    private static Guid MonthlyOperationId(int customerId,int year,int month)
    {
        var bytes=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{customerId}:{year:D4}-{month:D2}:monthly-allocation"));
        return new Guid(bytes.AsSpan(0,16));
    }
    private async Task RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        var rows=await db.FfpLedgerOutbox.Where(x=>x.State==FfpOutboxState.Processing).ToListAsync(cancellationToken);
        foreach(var row in rows){row.State=FfpOutboxState.Failed;row.LastError="Recovered after interrupted FFP publication.";}
        if(rows.Count>0) await db.SaveChangesAsync(cancellationToken);
    }
}
