namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class LocalCafeteriaDbContext(DbContextOptions<LocalCafeteriaDbContext> options) : CafeteriaDbContextBase(options)
{
    public DbSet<FfpLedgerOutboxRow> FfpLedgerOutbox => Set<FfpLedgerOutboxRow>();
    public DbSet<LocalSchemaInfoRow> LocalSchemaInfo => Set<LocalSchemaInfoRow>();
    public DbSet<ManagementEventInboxRow> ManagementEventInbox => Set<ManagementEventInboxRow>();
    public DbSet<PosTransactionOutboxRow> PosTransactionOutbox => Set<PosTransactionOutboxRow>();

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

        b.Entity<ManagementEventInboxRow>().ToTable("ManagementEventInbox");
        b.Entity<ManagementEventInboxRow>().HasKey(x => x.OperationId);
        b.Entity<ManagementEventInboxRow>().HasIndex(x => x.Sequence).IsUnique();
        b.Entity<ManagementEventInboxRow>().Property(x => x.PayloadJson).HasMaxLength(8000);
        b.Entity<ManagementEventInboxRow>().Property(x => x.LastError).HasMaxLength(2000);

        b.Entity<PosTransactionOutboxRow>().ToTable("PosTransactionOutbox");
        b.Entity<PosTransactionOutboxRow>().HasKey(x => x.EventId);
        b.Entity<PosTransactionOutboxRow>().HasIndex(x => new { x.PublishedAt, x.NextAttemptAt, x.CreatedAt });
        b.Entity<PosTransactionOutboxRow>().Property(x => x.Kind).HasMaxLength(64);
        b.Entity<PosTransactionOutboxRow>().Property(x => x.Actor).HasMaxLength(256);
        b.Entity<PosTransactionOutboxRow>().Property(x => x.TerminalId).HasMaxLength(128);
        b.Entity<PosTransactionOutboxRow>().Property(x => x.PayloadJson).HasMaxLength(16000);
        b.Entity<PosTransactionOutboxRow>().Property(x => x.Reason).HasMaxLength(2000);
        b.Entity<PosTransactionOutboxRow>().Property(x => x.LastError).HasMaxLength(2000);

        b.Entity<LocalSchemaInfoRow>().ToTable("LocalSchemaInfo");
        b.Entity<LocalSchemaInfoRow>().HasKey(x => x.Id);
    }
}
