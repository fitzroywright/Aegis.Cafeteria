namespace Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public sealed class ServiceDbContext(DbContextOptions<ServiceDbContext> options) : DbContext(options)
{
    public DbSet<ServiceEventRow> Events => Set<ServiceEventRow>();
    public DbSet<AuditTrailRow> AuditTrail => Set<AuditTrailRow>();
    public DbSet<CustomerCreditPolicyRow> CustomerCreditPolicies => Set<CustomerCreditPolicyRow>();
    public DbSet<ScheduledDispatchRow> ScheduledDispatches => Set<ScheduledDispatchRow>();
    public DbSet<CustomerNotificationPreferenceRow> CustomerNotificationPreferences => Set<CustomerNotificationPreferenceRow>();
    public DbSet<ManagerReportSettingsRow> ManagerReportSettings => Set<ManagerReportSettingsRow>();
    public DbSet<OutboundEmailRow> OutboundEmails => Set<OutboundEmailRow>();
    public DbSet<IntegrationEmployeeCacheRow> IntegrationEmployees => Set<IntegrationEmployeeCacheRow>();
    public DbSet<IntegrationStateRow> IntegrationState => Set<IntegrationStateRow>();
    public DbSet<ValidatedPolicyCacheRow> ValidatedPolicies => Set<ValidatedPolicyCacheRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ServiceEventRow>().ToTable("CafeteriaServiceEvents");
        b.Entity<ServiceEventRow>().HasKey(x => x.Sequence);
        b.Entity<ServiceEventRow>().Property(x => x.Sequence).ValueGeneratedOnAdd();
        b.Entity<ServiceEventRow>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<ServiceEventRow>().HasIndex(x => new { x.CustomerId, x.Kind, x.OccurredAt });
        b.Entity<ServiceEventRow>().HasIndex(x => x.ExternalPublishedAt);
        b.Entity<ServiceEventRow>().Property(x => x.PayloadJson).HasMaxLength(8000);
        b.Entity<ServiceEventRow>().Property(x => x.ExternalPublishedAt).HasColumnName("FfpPublishedAt");
        b.Entity<ServiceEventRow>().Property(x => x.PublicationAttemptCount).HasColumnName("FfpAttemptCount");
        b.Entity<ServiceEventRow>().Property(x => x.PublicationLastAttemptAt).HasColumnName("FfpLastAttemptAt");
        b.Entity<ServiceEventRow>().Property(x => x.PublicationLastError).HasColumnName("FfpLastError").HasMaxLength(2000);

        b.Entity<AuditTrailRow>().ToTable("AuditTrail");
        b.Entity<AuditTrailRow>().HasKey(x => x.Id);
        b.Entity<AuditTrailRow>().Property(x => x.Id).ValueGeneratedOnAdd();
        b.Entity<AuditTrailRow>().HasIndex(x => x.OccurredAt);
        b.Entity<AuditTrailRow>().HasIndex(x => x.Actor);
        b.Entity<AuditTrailRow>().HasIndex(x => new { x.EntityType, x.EntityId });
        b.Entity<AuditTrailRow>().HasIndex(x => x.CustomerId);
        b.Entity<AuditTrailRow>().HasIndex(x => x.OperationId);
        b.Entity<AuditTrailRow>().Property(x => x.Actor).HasMaxLength(256);
        b.Entity<AuditTrailRow>().Property(x => x.Action).HasMaxLength(256);
        b.Entity<AuditTrailRow>().Property(x => x.EntityType).HasMaxLength(128);
        b.Entity<AuditTrailRow>().Property(x => x.EntityId).HasMaxLength(256);
        b.Entity<AuditTrailRow>().Property(x => x.Source).HasMaxLength(128);
        b.Entity<AuditTrailRow>().Property(x => x.TerminalId).HasMaxLength(128);
        b.Entity<AuditTrailRow>().Property(x => x.CorrelationId).HasMaxLength(128);
        b.Entity<AuditTrailRow>().Property(x => x.BeforeJson).HasMaxLength(8000);
        b.Entity<AuditTrailRow>().Property(x => x.AfterJson).HasMaxLength(8000);
        b.Entity<AuditTrailRow>().Property(x => x.Detail).HasMaxLength(2000);

        b.Entity<CustomerCreditPolicyRow>().ToTable("CustomerCreditPolicies");
        b.Entity<CustomerCreditPolicyRow>().HasKey(x => x.CustomerId);
        b.Entity<CustomerCreditPolicyRow>().Property(x => x.AdditionalCreditLimit).HasPrecision(18, 2);
        b.Entity<CustomerCreditPolicyRow>().Property(x => x.UpdatedBy).HasMaxLength(256);

        b.Entity<CustomerNotificationPreferenceRow>().ToTable("CustomerNotificationPreferences");
        b.Entity<CustomerNotificationPreferenceRow>().HasKey(x => x.CustomerId);
        b.Entity<CustomerNotificationPreferenceRow>().Property(x => x.EmailAddress).HasMaxLength(320);

        b.Entity<ManagerReportSettingsRow>().ToTable("ManagerReportSettings");
        b.Entity<ManagerReportSettingsRow>().HasKey(x => x.Id);
        b.Entity<ManagerReportSettingsRow>().Property(x => x.EmailAddress).HasMaxLength(320);

        b.Entity<OutboundEmailRow>().ToTable("OutboundEmails");
        b.Entity<OutboundEmailRow>().HasKey(x => x.Id);
        b.Entity<OutboundEmailRow>().HasIndex(x => x.DispatchKey).IsUnique();
        b.Entity<OutboundEmailRow>().HasIndex(x => new { x.SentAt, x.NextAttemptAt });
        b.Entity<OutboundEmailRow>().Property(x => x.DispatchKey).HasMaxLength(256);
        b.Entity<OutboundEmailRow>().Property(x => x.Recipient).HasMaxLength(320);
        b.Entity<OutboundEmailRow>().Property(x => x.Subject).HasMaxLength(500);
        b.Entity<OutboundEmailRow>().Property(x => x.LastError).HasMaxLength(2000);


        b.Entity<IntegrationEmployeeCacheRow>().ToTable("IntegrationEmployeeCache");
        b.Entity<IntegrationEmployeeCacheRow>().HasKey(x => x.EmployeeId);
        b.Entity<IntegrationEmployeeCacheRow>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<IntegrationEmployeeCacheRow>().Property(x => x.CardIdentifier).HasMaxLength(256);
        b.Entity<IntegrationEmployeeCacheRow>().Property(x => x.Version).HasMaxLength(256);

        b.Entity<IntegrationStateRow>().ToTable("IntegrationState");
        b.Entity<IntegrationStateRow>().HasKey(x => x.Key);
        b.Entity<IntegrationStateRow>().Property(x => x.Key).HasMaxLength(128);
        b.Entity<IntegrationStateRow>().Property(x => x.Value).HasMaxLength(2000);

        b.Entity<ValidatedPolicyCacheRow>().ToTable("ValidatedPolicyCache");
        b.Entity<ValidatedPolicyCacheRow>().HasKey(x => new { x.Year, x.Month });
        b.Entity<ValidatedPolicyCacheRow>().Property(x => x.PayloadJson).HasMaxLength(12000);

        b.Entity<ScheduledDispatchRow>().HasKey(x => x.DispatchKey);
        b.Entity<ScheduledDispatchRow>().Property(x => x.DispatchKey).HasMaxLength(256);
    }
}
