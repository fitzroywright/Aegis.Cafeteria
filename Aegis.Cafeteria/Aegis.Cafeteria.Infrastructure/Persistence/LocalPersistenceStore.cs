namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class LocalPersistenceStore(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    ILogger<LocalPersistenceStore> logger)
{
    private readonly FinancialPostingService financialPostingService = new();

    public Task SaveCustomerAsync(CustomerRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.Customers.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.Customers.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveCustomerIdentifierAsync(CustomerIdentifierRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.CustomerIdentifiers.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.CustomerIdentifiers.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveProductAsync(ProductRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.Products.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.Products.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveOrderAsync(OrderRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveOrderAsync(db, row, ct), ct);
    public Task SaveAccountEventAsync(AccountEventRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveAccountEventAsync(db, row, ct), ct);
    public Task SaveAccountPeriodAsync(AccountPeriodRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.AccountPeriods.SingleOrDefaultAsync(v=>v.CustomerId==row.CustomerId&&v.Year==row.Year&&v.Month==row.Month,ct); if(x is null) db.AccountPeriods.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocationRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveMonthlyCreditAllocationAsync(db, row, ct), ct);
    public async Task<MonthlyCreditAllocationRow?> GetMonthlyCreditAllocationAsync(int customerId,int year,int month,CancellationToken ct=default)
    { await using var db=await factory.CreateDbContextAsync(ct); return await db.MonthlyCreditAllocations.AsNoTracking().SingleOrDefaultAsync(x=>x.CustomerId==customerId&&x.Year==year&&x.Month==month,ct); }
    private async Task SaveAsync(Func<LocalCafeteriaDbContext,Task> action,CancellationToken ct)
    { await using var db=await factory.CreateDbContextAsync(ct); await using var tx=await db.Database.BeginTransactionAsync(ct); await action(db); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); logger.LogDebug("Local cafeteria state committed"); }
}
