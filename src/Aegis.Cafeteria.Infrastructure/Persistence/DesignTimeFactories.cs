namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
public sealed class LocalDesignTimeFactory:IDesignTimeDbContextFactory<LocalCafeteriaDbContext>{ public LocalCafeteriaDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<LocalCafeteriaDbContext>().UseSqlite("Data Source=cafeteria.design.db").Options); }
