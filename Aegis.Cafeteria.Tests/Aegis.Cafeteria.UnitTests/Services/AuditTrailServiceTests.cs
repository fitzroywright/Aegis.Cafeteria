namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class AuditTrailServiceTests
{
    [Fact]
    public async Task RecordAsync_ChainsRows_AndDetectsTampering()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"cafeteria-audit-{Guid.NewGuid():N}.db");
        try
        {
            TestServiceDbContextFactory factory = new(databasePath);
            AuditTrailService service = new(factory);
            AuditTrailRow first = await service.RecordAsync("accounts.user", "Approve Additional Credit", "AdditionalCredit", entityId: "10", customerId: 10, operationId: Guid.NewGuid(), after: new { Amount = 500m });
            AuditTrailRow second = await service.RecordAsync("accounts.user", "Process Stipend", "Stipend", entityId: "10", customerId: 10, operationId: Guid.NewGuid(), after: new { CashAmount = 800m, CardReduction = 800m });
            Assert.False(string.IsNullOrWhiteSpace(first.Hash));
            Assert.Equal(first.Hash, second.PreviousHash);
            Assert.False(string.IsNullOrWhiteSpace(second.Hash));
            AuditIntegrityResult valid = await service.VerifyIntegrityAsync();
            Assert.True(valid.IsValid);
            Assert.Equal(2, valid.VerifiedRows);

            await using (ServiceDbContext database = await factory.CreateDbContextAsync())
            {
                await database.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditTrail SET Detail = {"tampered"} WHERE Id = {first.Id};");
            }

            AuditIntegrityResult invalid = await service.VerifyIntegrityAsync();
            Assert.False(invalid.IsValid);
            Assert.Equal(first.Id, invalid.FirstInvalidAuditId);
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private sealed class TestServiceDbContextFactory : IDbContextFactory<ServiceDbContext>
    {
        private readonly DbContextOptions<ServiceDbContext> options;
        public TestServiceDbContextFactory(string databasePath) => this.options = new DbContextOptionsBuilder<ServiceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        public ServiceDbContext CreateDbContext() => new(this.options);
        public Task<ServiceDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
