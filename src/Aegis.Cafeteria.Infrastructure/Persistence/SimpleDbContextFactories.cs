namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
public sealed class LocalDbContextFactory(DbContextOptions<LocalCafeteriaDbContext> options):IDbContextFactory<LocalCafeteriaDbContext>{ public LocalCafeteriaDbContext CreateDbContext()=>new(options); public Task<LocalCafeteriaDbContext> CreateDbContextAsync(CancellationToken cancellationToken=default)=>Task.FromResult(CreateDbContext()); }
