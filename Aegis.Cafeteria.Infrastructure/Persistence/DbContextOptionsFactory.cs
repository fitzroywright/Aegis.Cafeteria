namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
public static class DbContextOptionsFactory
{
 public static DbContextOptions<LocalCafeteriaDbContext> CreateLocal(CafeteriaPersistenceOptions options)
  => new DbContextOptionsBuilder<LocalCafeteriaDbContext>().UseSqlite(options.LocalSqliteConnectionString).Options;
}
