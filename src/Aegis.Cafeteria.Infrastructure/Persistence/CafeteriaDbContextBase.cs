namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public abstract class CafeteriaDbContextBase(DbContextOptions options) : DbContext(options)
{
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    public DbSet<CustomerIdentifierRow> CustomerIdentifiers => Set<CustomerIdentifierRow>();
    public DbSet<HolidayRow> Holidays => Set<HolidayRow>();
    public DbSet<CafeteriaRatePolicyRow> CafeteriaRatePolicies => Set<CafeteriaRatePolicyRow>();
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<OrderRow> Orders => Set<OrderRow>();
    public DbSet<OrderItemRow> OrderItems => Set<OrderItemRow>();
    public DbSet<OrderPaymentRow> OrderPayments => Set<OrderPaymentRow>();
    public DbSet<OrderRefundRow> OrderRefunds => Set<OrderRefundRow>();
    public DbSet<OrderRefundItemRow> OrderRefundItems => Set<OrderRefundItemRow>();
    public DbSet<RefundAllocationRow> RefundAllocations => Set<RefundAllocationRow>();
    public DbSet<AccountEventRow> AccountEvents => Set<AccountEventRow>();
    public DbSet<AccountPeriodRow> AccountPeriods => Set<AccountPeriodRow>();
    public DbSet<MonthlyCreditAllocationRow> MonthlyCreditAllocations => Set<MonthlyCreditAllocationRow>();
    public DbSet<WeeklySummaryDispatchRow> WeeklySummaryDispatches => Set<WeeklySummaryDispatchRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CustomerRow>().HasKey(x => x.Id);
        b.Entity<CustomerIdentifierRow>().HasKey(x => x.Id);
        // This is an Aegis synchronization cache, not the organization's existing holiday source-of-truth table.
        b.Entity<HolidayRow>().ToTable("CafeteriaHolidayCache");
        b.Entity<HolidayRow>().HasKey(x => x.Date);
        b.Entity<CafeteriaRatePolicyRow>().ToTable("CafeteriaRatePolicy");
        b.Entity<CafeteriaRatePolicyRow>().HasKey(x => x.Id);
        b.Entity<ProductRow>().HasKey(x => x.Id);
        b.Entity<OrderRow>().HasKey(x => x.Id);
        b.Entity<OrderItemRow>().HasKey(x => x.Id);
        b.Entity<OrderPaymentRow>().HasKey(x => x.Id);
        b.Entity<OrderRefundRow>().HasKey(x => x.Id);
        b.Entity<OrderRefundItemRow>().HasKey(x => x.Id);
        b.Entity<RefundAllocationRow>().HasKey(x => x.Id);
        b.Entity<AccountEventRow>().HasKey(x => x.Id);
        b.Entity<AccountPeriodRow>().HasKey(x => new { x.CustomerId, x.Year, x.Month });
        b.Entity<MonthlyCreditAllocationRow>().HasKey(x => new { x.CustomerId, x.Year, x.Month });
        b.Entity<WeeklySummaryDispatchRow>().HasKey(x => new { x.CustomerId, x.WeekStart });

        // Explicit lengths keep the SQL Server schema indexable and document the persisted contract.
        b.Entity<CustomerRow>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<CustomerRow>().Property(x => x.NormalizedName).HasMaxLength(200);
        b.Entity<CustomerIdentifierRow>().Property(x => x.Value).HasMaxLength(256);
        b.Entity<CustomerIdentifierRow>().Property(x => x.NormalizedValue).HasMaxLength(256);
        b.Entity<HolidayRow>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<CafeteriaRatePolicyRow>().Property(x => x.CreditLimitLunchItemCode).HasMaxLength(32);
        b.Entity<CafeteriaRatePolicyRow>().Property(x => x.SubsidyLunchItemCode).HasMaxLength(32);
        b.Entity<ProductRow>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<OrderRow>().Property(x => x.VoidedBy).HasMaxLength(128);
        b.Entity<OrderRow>().Property(x => x.VoidReason).HasMaxLength(1000);
        b.Entity<OrderRow>().Property(x => x.CashierUserId).HasMaxLength(128);
        b.Entity<OrderRow>().Property(x => x.TerminalId).HasMaxLength(128);
        b.Entity<OrderItemRow>().Property(x => x.ProductName).HasMaxLength(200);
        b.Entity<OrderRefundRow>().Property(x => x.RefundedBy).HasMaxLength(128);
        b.Entity<OrderRefundRow>().Property(x => x.Reason).HasMaxLength(1000);
        b.Entity<AccountEventRow>().Property(x => x.EventType).HasMaxLength(100);
        b.Entity<AccountPeriodRow>().Property(x => x.ClosedBy).HasMaxLength(128);

        b.Entity<CustomerIdentifierRow>().HasOne<CustomerRow>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderRow>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderRow>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderRow>().HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderRefundRow>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderRefundRow>().HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.Cascade);

        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            p.SetPrecision(18);
            p.SetScale(2);
        }

        b.Entity<CustomerRow>().HasIndex(x => x.NormalizedName);
        b.Entity<CustomerIdentifierRow>().HasIndex(x => new { x.Type, x.NormalizedValue }).IsUnique();
        b.Entity<CustomerIdentifierRow>().HasIndex(x => x.CustomerId);
        b.Entity<OrderRow>().HasIndex(x => new { x.CustomerId, x.AccountYear, x.AccountMonth });
        b.Entity<OrderRow>().HasIndex(x => x.CompletedAt);
        b.Entity<OrderRow>().HasIndex(x => new { x.TerminalId, x.CompletedAt });
        b.Entity<OrderRow>().HasIndex(x => new { x.CashierUserId, x.CompletedAt });
        b.Entity<OrderRefundRow>().HasIndex(x => x.RefundedAt);
        b.Entity<ProductRow>().HasIndex(x => x.Name);
        b.Entity<AccountEventRow>().HasIndex(x => new { x.CustomerId, x.AccountYear, x.AccountMonth, x.OccurredAt });
        b.Entity<MonthlyCreditAllocationRow>().HasIndex(x => new { x.Year, x.Month });
    }
}
