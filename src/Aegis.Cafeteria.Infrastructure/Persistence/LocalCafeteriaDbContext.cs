namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class LocalCafeteriaDbContext(DbContextOptions<LocalCafeteriaDbContext> options) : CafeteriaDbContextBase(options)
{
    public DbSet<FfpLedgerOutboxRow> FfpLedgerOutbox => Set<FfpLedgerOutboxRow>();
    public DbSet<FfpImportedTransactionRow> FfpImportedTransactions => Set<FfpImportedTransactionRow>();
    public DbSet<LocalSchemaInfoRow> LocalSchemaInfo => Set<LocalSchemaInfoRow>();
    public DbSet<ManagementEventInboxRow> ManagementEventInbox => Set<ManagementEventInboxRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<FfpLedgerOutboxRow>().HasKey(x => x.OperationId);
        b.Entity<FfpLedgerOutboxRow>().HasIndex(x => new { x.State, x.OccurredAt });
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.Description).HasMaxLength(1000);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.ScanCode).HasMaxLength(50);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.UserName).HasMaxLength(128);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.IdName).HasMaxLength(128);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.Code).HasMaxLength(256);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.CreatedBy).HasMaxLength(128);
        b.Entity<FfpLedgerOutboxRow>().Property(x => x.LastError).HasMaxLength(2000);

        b.Entity<FfpImportedTransactionRow>().ToTable("FfpImportedTransactions");
        b.Entity<FfpImportedTransactionRow>().HasKey(x => x.SourceId);
        b.Entity<FfpImportedTransactionRow>().HasIndex(x => new { x.CustomerId, x.Year, x.Month, x.OccurredAt });
        b.Entity<FfpImportedTransactionRow>().Property(x => x.IdName).HasMaxLength(128);
        b.Entity<FfpImportedTransactionRow>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<FfpImportedTransactionRow>().Property(x => x.UserName).HasMaxLength(128);
        b.Entity<FfpImportedTransactionRow>().Property(x => x.ScanCode).HasMaxLength(128);

        b.Entity<ManagementEventInboxRow>().ToTable("ManagementEventInbox");
        b.Entity<ManagementEventInboxRow>().HasKey(x => x.OperationId);
        b.Entity<ManagementEventInboxRow>().HasIndex(x => x.Sequence).IsUnique();
        b.Entity<ManagementEventInboxRow>().Property(x => x.PayloadJson).HasMaxLength(8000);
        b.Entity<ManagementEventInboxRow>().Property(x => x.LastError).HasMaxLength(2000);
        b.Entity<LocalSchemaInfoRow>().ToTable("LocalSchemaInfo");
        b.Entity<LocalSchemaInfoRow>().HasKey(x => x.Id);
    }
}
