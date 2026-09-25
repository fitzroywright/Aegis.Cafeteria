using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Reporting;
using Aegis.Cafeteria.Services.Realtime;
using Aegis.Cafeteria.Services.Workers;
using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Aegis.Engineering.Diagnostics;
using Common.Diagnostics;
using Common.Registration;
using Common.Secrets;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Cafeteria.Services;

internal static class CafeteriaServicesHost
{
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Host.UseWindowsService(options => options.ServiceName = "Aegis.Cafeteria.Services");
        builder.Configuration.AddEnvironmentVariables();

        ManagementApiOptions management = builder.Configuration.GetSection("ManagementApi").Get<ManagementApiOptions>() ?? new();
        MonthOpeningOptions monthOpening = builder.Configuration.GetSection("MonthOpening").Get<MonthOpeningOptions>() ?? new();
        ScheduledEmailOptions scheduledEmail = builder.Configuration.GetSection("ScheduledEmail").Get<ScheduledEmailOptions>() ?? new();
        ExternalPublicationOptions externalPublication = builder.Configuration.GetSection("ExternalPublication").Get<ExternalPublicationOptions>() ?? new();
        string serviceDb = CafeteriaServiceDatabase.ResolveConnectionString(builder.Configuration);

        RegisterOptions(builder.Services, management, monthOpening, scheduledEmail, externalPublication);
        builder.Services.AddHttpClient("CafeteriaIntegrationPlane");
        IntegrationPluginLoader.Register(builder);
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

        builder.Services.AddCommonDiagnostics();
        builder.Services.AddSingleton<ISecretProvider>(_ =>
            CommonSecretProviderFactory.Create(builder.Configuration));
        builder.Services.AddSingleton<
            IDiagnosticLevelRequestCredentialProvider,
            CommonSecretsDiagnosticLevelRequestCredentialProvider>();
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
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaSecretsCredentialDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaPublicationQueueDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaLedgerDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaMessagingDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaFundingPolicyDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaCustomerLookupDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaRecoveryReadinessDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaExhaustiveConsistencyDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaOutboundEmailQueueDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaReportDeliveryConfigurationDiagnosticLevelTest>();
        builder.Services.AddSingleton<IDiagnosticLevelLocalTest, CafeteriaAuditTrailIntegrityDiagnosticLevelTest>();
    }

    private static void RegisterOptions
    (
          IServiceCollection services
        , ManagementApiOptions management
        , MonthOpeningOptions monthOpening
        , ScheduledEmailOptions scheduledEmail
        , ExternalPublicationOptions externalPublication
    )
    {
        services.AddSingleton(management);
        services.AddSingleton(monthOpening);
        services.AddSingleton(scheduledEmail);
        services.AddSingleton(externalPublication);
    }

    private static void RegisterIntegrationGateways(IServiceCollection services)
    {
        services.AddSingleton<MonthlyAllocationCalculator>();
        services.AddSingleton<CafeteriaLedgerQuery>();
        services.AddSingleton<CafeteriaMonthRecalculationGuard>();
        services.AddSingleton<IMonthRecalculationGuard>(sp => sp.GetRequiredService<CafeteriaMonthRecalculationGuard>());
        services.AddSingleton<CafeteriaPayrollLedgerSource>();
        services.AddSingleton<IPayrollLedgerSource>(sp => sp.GetRequiredService<CafeteriaPayrollLedgerSource>());
    }

    private static void RegisterApplicationServices(IServiceCollection services, string serviceDb)
    {
        services.AddDbContextFactory<ServiceDbContext>(options => options.UseSqlite(serviceDb));
        services.AddSignalR();
        services.AddSingleton<ServiceEventRealtimePublisher>();
        services.AddSingleton<CafeteriaOperationsFlowPublisher>();
        services.AddHostedService(sp => sp.GetRequiredService<CafeteriaOperationsFlowPublisher>());
        services.AddSingleton<ServiceEventStore>();
        services.AddSingleton<ServiceIntegrationStateStore>();
        services.AddSingleton<IEmployeeFactCache>(sp => sp.GetRequiredService<ServiceIntegrationStateStore>());
        services.AddSingleton<ICafeteriaPolicyCache>(sp => sp.GetRequiredService<ServiceIntegrationStateStore>());
        services.AddSingleton<CustomerCreditPolicyStore>();
        services.AddHttpContextAccessor();
        services.AddScoped<IAdditionalCreditAuthorizer, AuthenticatedAdditionalCreditAuthorizer>();
        services.AddScoped<AdditionalCreditService>();
        services.AddScoped<IStipendAuthorizer, AuthenticatedStipendAuthorizer>();
        services.AddScoped<StipendService>();
        services.AddSingleton<StipendAccountSnapshotFactory>();
        services.AddSingleton<EngineeringDiagnosticRunStore>();
        services.AddSingleton<CafeteriaEngineeringDiagnosticService>();
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
                policy.RequireAssertion(context =>
                    context.User.IsInRole("CafeteriaManager")
                    || management.AuthorizedGroups.Length == 0
                    || management.AuthorizedGroups.Any(context.User.IsInRole));
            });
            options.AddPolicy("EngineeringDiagnostics", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.IsInRole("CafeteriaManager")
                    || management.AuthorizedGroups.Length == 0
                    || management.AuthorizedGroups.Any(context.User.IsInRole));
            });
        });
    }

    private static void RegisterWorkers(IServiceCollection services)
    {
        services.AddSingleton<CafeteriaPolicyStartupWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<CafeteriaPolicyStartupWorker>());
        services.AddSingleton<MonthOpeningWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<MonthOpeningWorker>());
        services.AddSingleton<MonthClosingWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<MonthClosingWorker>());
        services.AddSingleton<ExternalPublicationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ExternalPublicationWorker>());
        services.AddSingleton<CafeteriaEmailReportSource>();
        services.AddSingleton<PayrollDeductionReportSource>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<ScheduledEmailWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ScheduledEmailWorker>());
    }
}