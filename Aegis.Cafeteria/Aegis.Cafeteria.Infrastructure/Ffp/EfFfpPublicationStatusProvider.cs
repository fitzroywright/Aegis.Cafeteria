namespace Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
public sealed class EfFfpPublicationStatusProvider(IDbContextFactory<LocalCafeteriaDbContext> factory,FfpIntegrationOptions options):IFfpPublicationStatusProvider
{
 public async Task<FfpPublicationStatus> GetAsync(CancellationToken ct=default){
  if(!options.Enabled)return new(false,0,0,null,null);
  await using var db=await factory.CreateDbContextAsync(ct); var rows=await db.FfpLedgerOutbox.AsNoTracking().ToListAsync(ct);
  var pending=rows.Count(x=>x.State is FfpOutboxState.Pending or FfpOutboxState.Failed or FfpOutboxState.Processing); var poisoned=rows.Count(x=>x.State==FfpOutboxState.Poisoned);
  var last=rows.Where(x=>x.State==FfpOutboxState.Succeeded&&x.LastAttemptAt.HasValue).Select(x=>x.LastAttemptAt).OrderByDescending(x=>x).FirstOrDefault();
  var error=rows.Where(x=>x.State is FfpOutboxState.Failed or FfpOutboxState.Poisoned).OrderByDescending(x=>x.LastAttemptAt).Select(x=>x.LastError).FirstOrDefault();
  return new(true,pending,poisoned,last,error);
 }
}
