using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Services.Development;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Realtime;
using Aegis.Cafeteria.Services.Workers;
using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Aegis.Engineering.Diagnostics;
using Common.Diagnostics;
using Common.Registration;
using Common.Secrets;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Cafeteria.Services;

internal static class CafeteriaServicesHost
{
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Host.UseWindowsService(options => options.ServiceName = "Aegis.Cafeteria.Services");
        builder.Configuration.AddEnvironmentVariables();

        FfpIntegrationOptions ffp = BuildFfpIntegration(builder);
        ManagementApiOptions management = builder.Configuration.GetSection("ManagementApi").Get<ManagementApiOptions>() ?? new();
        MonthOpeningOptions monthOpening = builder.Configuration.GetSection("MonthOpening").Get<MonthOpeningOptions>() ?? new();
        ScheduledEmailOptions scheduledEmail = builder.Configuration.GetSection("ScheduledEmail").Get<ScheduledEmailOptions>() ?? new();
        ExternalPublicationOptions externalPublication = builder.Configuration.GetSection("ExternalPublication").Get<ExternalPublicationOptions>() ?? new();
        string serviceDb = CafeteriaServiceDatabase.ResolveConnectionString(builder.Configuration);

        RegisterOptions(builder.Services, ffp, management, monthOpening, scheduledEmail, externalPublication);
        RegisterDiagnosticLevel(builder);
        RegisterIntegrationGateways(builder.Services);
        RegisterApplicationServices(builder.Services, serviceDb);
        ConfigureDevelopmentServices(builder);
        ConfigureAuthentication(builder, management);
        RegisterWorkers(builder.Services);
    }

    private static void RegisterDiagnosticLevel(WebApplicationBuilder builder)
    {
        string instanceId = builder.Configuration["Service:Identity"]?.Trim()
            ?? builder.Environment.EnvironmentName;
        string storePath = builder.Configuration["Aegis:Diagnostics:StorePath"]?.Trim()
            ?? Path.Combine(builder.Environment.ContentRootPath, "diagnostics", "diagnostic-level.db");

        builder.Services.AddCommonSecrets(builder.Configuration);
        builder.Services.AddCommonDiagnostics();
        builder.Services.AddSingleton(new DiagnosticLevelExecutionOptions
        {
            Application = "Aegis.Cafeteria.Services",
            Component = "Services",
            InstanceId = instanceId,
            CatalogVersion = "1"
        });
        builder.Services.AddSingleton(new DiagnosticLevelStoreOptions
        {
            Path = storePath
        });
        builder.Services.AddCommonSecretsDiagnostics();

        string configurationUrl = AegisControlPlaneEndpoints.ResolvePublic(
            builder.Configuration,
            AegisControlPlaneService.Configuration,
            configurationKey: "Aegis:Configuration:Url");
        if (!Uri.TryCreate(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri? configurationUri))
            throw new InvalidOperationException("Aegis Configuration URL is invalid.");

        string identityFile = builder.Configuration["Aegis:Registration:IdentityFile"]?.Trim()
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Aegis",
                    "Cafeteria.Services",
                    "registration-identity.json")
                : "/var/lib/aegis/cafeteria-services/registration-identity.json");

        builder.Services.AddCommonRegistrationDiagnosticLevel(new RegistrationLifecycleOptions(
            configurationUri,
            "Aegis.Cafeteria.Services",
            instanceId,
            identityFile,
            TimeSpan.FromSeconds(Math.Clamp(
                builder.Configuration.GetValue("Aegis:Registration:RequestTimeoutSeconds", 30),
                5,
                120)),
            RunbookReference: "docs/RUNBOOK.md"));

        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaDatabaseConnectivityDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaDatabaseIntegrityDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaConfigurationDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaRuntimeIdentityDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaDiagnosticStoreIntegrityDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaWorkforceSourceDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaHolidaySourceDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaPublicationQueueDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaFundingPolicyDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaCustomerLookupDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaRecoveryReadinessDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaExhaustiveConsistencyDiagnosticLevelTest>();
    }

    private static FfpIntegrationOptions BuildFfpIntegration(WebApplicationBuilder builder)
    {
        IConfigurationSection section = builder.Configuration.GetSection("FfpIntegration");
        FfpIntegrationOptions configured = section.Get<FfpIntegrationOptions>() ?? new FfpIntegrationOptions();

        if (!configured.Enabled)
            throw new InvalidOperationException("Aegis.Cafeteria.Services requires FFP integration in every runtime environment.");

        string server = section["Server"]?.Trim() ?? string.Empty;
        string database = section["Database"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("FfpIntegration:Server and FfpIntegration:Database are required. Cafeteria Services uses Windows Integrated Security and does not accept stored SQL credentials.");

        SqlConnectionStringBuilder connection = new()
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = section.GetValue("Encrypt", true),
            TrustServerCertificate = section.GetValue("TrustServerCertificate", false),
            ApplicationName = "Aegis.Cafeteria.Services"
        };

        return new FfpIntegrationOptions
        {
            Enabled = configured.Enabled,
            ConnectionString = connection.ConnectionString,
            AllowWrites = configured.AllowWrites,
            RefreshIntervalMinutes = configured.RefreshIntervalMinutes,
            CreditLimitLunchItemCode = configured.CreditLimitLunchItemCode,
            SubsidyLunchItemCode = configured.SubsidyLunchItemCode,
            MonthlyAllocationScanType = configured.MonthlyAllocationScanType,
            PurchaseScanType = configured.PurchaseScanType,
            StipendScanType = configured.StipendScanType,
            RefundScanType = configured.RefundScanType,
            AdjustmentScanType = configured.AdjustmentScanType
        };
    }

    private static void RegisterOptions
    (
          IServiceCollection services
        , FfpIntegrationOptions ffp
        , ManagementApiOptions management
        , MonthOpeningOptions monthOpening
        , ScheduledEmailOptions scheduledEmail
        , ExternalPublicationOptions externalPublication
    )
    {
        services.AddSingleton(ffp);
        services.AddSingleton(management);
        services.AddSingleton(monthOpening);
        services.AddSingleton(scheduledEmail);
        services.AddSingleton(externalPublication);
    }

    private static void RegisterIntegrationGateways(IServiceCollection services)
    {
        services.AddSingleton<MonthlyAllocationCalculator>();
        services.AddSingleton<FfpSqlApplicationGateway>();
        services.AddSingleton<FfpLunchItemBootstrapSource>();
        services.AddSingleton<FfpMonthImportSource>();
        services.AddSingleton<IExternalAccountingGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
        services.AddSingleton<IWorkforceReferenceSource>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
        services.AddSingleton<ILunchItemBootstrapSource>(sp => sp.GetRequiredService<FfpLunchItemBootstrapSource>());
        services.AddSingleton<IFfpMonthImportSource>(sp => sp.GetRequiredService<FfpMonthImportSource>());
        services.AddSingleton<IFundingPolicyCompatibilityGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
        services.AddSingleton<IMonthRecalculationGuard>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
        services.AddSingleton<IDevelopmentDataResetGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
        services.AddSingleton<FfpPayrollLedgerSource>();
        services.AddSingleton<IPayrollLedgerSource>(sp => sp.GetRequiredService<FfpPayrollLedgerSource>());
    }

    private static void RegisterApplicationServices(IServiceCollection services, string serviceDb)
    {
        services.AddDbContextFactory<ServiceDbContext>(options => options.UseSqlite(serviceDb));
        services.AddSignalR();
        services.AddSingleton<ServiceEventRealtimePublisher>();
        services.AddSingleton<CafeteriaOperationsFlowPublisher>();
        services.AddHostedService(sp => sp.GetRequiredService<CafeteriaOperationsFlowPublisher>());
        services.AddSingleton<ServiceEventStore>();
        services.AddSingleton<CustomerCreditPolicyStore>();
        services.AddSingleton<CafeteriaFundingPolicyStore>();
        services.AddSingleton<FundingPolicyManagementService>();
        services.AddSingleton<ICafeteriaFundingPolicySource>(sp => sp.GetRequiredService<CafeteriaFundingPolicyStore>());
        services.AddHttpContextAccessor();
        services.AddScoped<IAdditionalCreditAuthorizer, AuthenticatedAdditionalCreditAuthorizer>();
        services.AddScoped<AdditionalCreditService>();
        services.AddScoped<IStipendAuthorizer, AuthenticatedStipendAuthorizer>();
        services.AddScoped<StipendService>();
        services.AddSingleton<StipendAccountSnapshotFactory>();
        services.AddSingleton<EngineeringDiagnosticRunStore>();
        services.AddSingleton<CafeteriaEngineeringDiagnosticService>();
        services.AddSingleton<DevelopmentDataResetService>();
    }

    private static void ConfigureDevelopmentServices(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment()) return;
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddCors(options => options.AddPolicy(
            "DevelopmentManagement",
            policy => policy.WithOrigins("http://localhost:5090").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }

    private static void ConfigureAuthentication(WebApplicationBuilder builder, ManagementApiOptions management)
    {
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("CafeteriaPos", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("aegis_authentication_source", "Common.Security");
            });
            options.AddPolicy("CafeteriaManagement", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => management.AuthorizedGroups.Length == 0 || management.AuthorizedGroups.Any(context.User.IsInRole));
            });
            options.AddPolicy("EngineeringDiagnostics", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => management.AuthorizedGroups.Length == 0 || management.AuthorizedGroups.Any(context.User.IsInRole));
            });
        });
    }

    private static void RegisterWorkers(IServiceCollection services)
    {
        services.AddSingleton<MonthOpeningWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<MonthOpeningWorker>());
        services.AddSingleton<ServiceFfpPublisherWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ServiceFfpPublisherWorker>());
        services.AddSingleton<FfpEmailReportSource>();
        services.AddSingleton<PayrollDeductionReportSource>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<ScheduledEmailWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledEmailWorker>());
    }
}