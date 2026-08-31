namespace Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public sealed class ServiceDbContext(DbContextOptions<ServiceDbContext> options) : DbContext(options)
{
    public DbSet<ServiceEventRow> Events => Set<ServiceEventRow>();
    public DbSet<CustomerCreditPolicyRow> CustomerCreditPolicies => Set<CustomerCreditPolicyRow>();
    public DbSet<ScheduledDispatchRow> ScheduledDispatches => Set<ScheduledDispatchRow>();
    public DbSet<CafeteriaFundingPolicyRow> FundingPolicies => Set<CafeteriaFundingPolicyRow>();
    public DbSet<CafeteriaFundingPolicyHistoryRow> FundingPolicyHistory => Set<CafeteriaFundingPolicyHistoryRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ServiceEventRow>().ToTable("CafeteriaServiceEvents");
        b.Entity<ServiceEventRow>().HasKey(x => x.Sequence);
        b.Entity<ServiceEventRow>().Property(x => x.Sequence).ValueGeneratedOnAdd();
        b.Entity<ServiceEventRow>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<ServiceEventRow>().HasIndex(x => new { x.CustomerId, x.Kind, x.OccurredAt });
        b.Entity<ServiceEventRow>().HasIndex(x => x.FfpPublishedAt);
        b.Entity<ServiceEventRow>().Property(x => x.PayloadJson).HasMaxLength(8000);
        b.Entity<ServiceEventRow>().Property(x => x.FfpLastError).HasMaxLength(2000);

        b.Entity<CustomerCreditPolicyRow>().ToTable("CustomerCreditPolicies");
        b.Entity<CustomerCreditPolicyRow>().HasKey(x => x.CustomerId);
        b.Entity<CustomerCreditPolicyRow>().Property(x => x.AdditionalCreditLimit).HasPrecision(18, 2);
        b.Entity<CustomerCreditPolicyRow>().Property(x => x.UpdatedBy).HasMaxLength(256);

        b.Entity<CafeteriaFundingPolicyRow>().ToTable("CafeteriaFundingPolicy");
        b.Entity<CafeteriaFundingPolicyRow>().HasKey(x => x.Id);
        b.Entity<CafeteriaFundingPolicyRow>().Property(x => x.DailyCreditLimitRate).HasPrecision(18, 2);
        b.Entity<CafeteriaFundingPolicyRow>().Property(x => x.DailyCompanySubsidyRate).HasPrecision(18, 2);
        b.Entity<CafeteriaFundingPolicyRow>().Property(x => x.UpdatedBy).HasMaxLength(256);

        b.Entity<CafeteriaFundingPolicyHistoryRow>().ToTable("CafeteriaFundingPolicyHistory");
        b.Entity<CafeteriaFundingPolicyHistoryRow>().HasKey(x => x.Id);
        b.Entity<CafeteriaFundingPolicyHistoryRow>().Property(x => x.Id).ValueGeneratedOnAdd();
        b.Entity<CafeteriaFundingPolicyHistoryRow>().Property(x => x.DailyCreditLimitRate).HasPrecision(18, 2);
        b.Entity<CafeteriaFundingPolicyHistoryRow>().Property(x => x.DailyCompanySubsidyRate).HasPrecision(18, 2);
        b.Entity<CafeteriaFundingPolicyHistoryRow>().Property(x => x.UpdatedBy).HasMaxLength(256);

        b.Entity<ScheduledDispatchRow>().HasKey(x => x.DispatchKey);
        b.Entity<ScheduledDispatchRow>().Property(x => x.DispatchKey).HasMaxLength(256);
    }
}
