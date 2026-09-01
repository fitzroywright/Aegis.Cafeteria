namespace Aegis.Cafeteria.Infrastructure;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Messaging;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Health;
using Aegis.Cafeteria.Infrastructure.Messaging;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class CafeteriaInfrastructure
{
    public static IServiceCollection AddCafeteriaPos
    (
          this IServiceCollection services
        , CafeteriaRuntimeOptions runtime
    )
    {
        if (!runtime.Services.Enabled)
        {
            throw new InvalidOperationException
            (
                "Aegis.Cafeteria POS requires Aegis.Cafeteria.Services. Direct FFP SQL access from POS terminals is no longer supported."
            );
        }

        services.AddSingleton(runtime);
        services.AddSingleton(runtime.Persistence);
        services.AddSingleton(runtime.FfpIntegration);
        services.AddSingleton(runtime.Health);
        services.AddSingleton(runtime.Messaging);
        services.AddSingleton(runtime.Services);

        services.AddSingleton
        (
            new LocalDbContextFactory
            (
                DbContextOptionsFactory.CreateLocal(runtime.Persistence)
            )
        );

        services.AddSingleton<IDbContextFactory<LocalCafeteriaDbContext>>
        (
            serviceProvider => serviceProvider.GetRequiredService<LocalDbContextFactory>()
        );

        services.AddSingleton<LocalPersistenceStore>();
        services.AddSingleton<EfCafeteriaPersistenceStore>();
        services.AddSingleton<IMonthlyCreditAllocationStore>
        (
            serviceProvider => serviceProvider.GetRequiredService<EfCafeteriaPersistenceStore>()
        );
        services.AddSingleton<ICafeteriaPersistenceStore>
        (
            serviceProvider => new Aegis.Cafeteria.Infrastructure.Logging.LoggingCafeteriaPersistenceStore
            (
                  serviceProvider.GetRequiredService<EfCafeteriaPersistenceStore>()
                , serviceProvider.GetRequiredService<ILogger<Aegis.Cafeteria.Infrastructure.Logging.LoggingCafeteriaPersistenceStore>>()
            )
        );

        services.AddSingleton<IHolidayProvider, EfHolidayProvider>();
        services.AddSingleton<IWorkingDayProvider, StandardWorkingDayProvider>();
        services.AddSingleton<ICafeteriaRatePolicyProvider, EfCafeteriaRatePolicyProvider>();
        services.AddSingleton<IFfpPublicationStatusProvider, EfFfpPublicationStatusProvider>();
        services.AddSingleton<ICustomerAccountQuery, EfCustomerAccountQuery>();
        services.AddSingleton<ICustomerIdentificationService, EfCustomerIdentificationService>();
        services.AddSingleton<IProductCatalogQuery, EfProductCatalogQuery>();
        services.AddSingleton<IProductCatalogMaintenance>
        (
            serviceProvider => new EfProductCatalogMaintenance
            (
                  serviceProvider.GetRequiredService<IDbContextFactory<LocalCafeteriaDbContext>>()
                , serviceProvider.GetService<ILunchItemBootstrapSource>()
            )
        );
        services.AddSingleton<IOrderQuery, EfOrderQuery>();
        services.AddSingleton<IRecentTransactionQuery, EfRecentTransactionQuery>();
        services.AddSingleton<ICustomerReferenceCacheStatus, EfCustomerReferenceCacheStatus>();
        services.AddSingleton<CustomerAccountChangeNotifier>();
        services.AddSingleton<ICustomerAccountChangeNotifier>
        (
            serviceProvider => serviceProvider.GetRequiredService<CustomerAccountChangeNotifier>()
        );

        if (runtime.FfpIntegration.Enabled)
        {
            ConfigureFfpIntegration
            (
                  services
                , runtime
            );
        }

        services.AddSingleton<MonthlyAllocationCalculator>();
        services.AddSingleton<IMonthlyAllocationExecutionPolicy, MonthlyAllocationExecutionPolicy>();
        services.AddSingleton<MonthlyCreditAllocationService>();
        services.AddSingleton<PosCheckoutService>();
        services.AddSingleton<EfWeeklyTransactionSummaryQuery>();
        services.AddSingleton<IWeeklyTransactionSummaryQuery>
        (
            serviceProvider => serviceProvider.GetRequiredService<EfWeeklyTransactionSummaryQuery>()
        );
        services.AddSingleton<IWeeklySummaryDispatchStore>
        (
            serviceProvider => serviceProvider.GetRequiredService<EfWeeklyTransactionSummaryQuery>()
        );
        services.AddSingleton<WeeklyCustomerSummaryService>();

        if (runtime.Messaging.Enabled)
        {
            services.AddHttpClient<ICafeteriaMessagingGateway, HttpCafeteriaMessagingGateway>();
        }
        else
        {
            services.AddSingleton<ICafeteriaMessagingGateway, DisabledCafeteriaMessagingGateway>();
        }

        ConfigureCafeteriaServicesIntegration
        (
              services
            , runtime.Services
        );

        services.AddSingleton<PersistenceInitializer>();
        services.AddHostedService<CafeteriaHeartbeatWorker>();

        return services;
    }

    private static void ConfigureFfpIntegration
    (
          IServiceCollection services
        , CafeteriaRuntimeOptions runtime
    )
    {
        services.AddHttpClient<ServicesIntegrationGateway>
        (
            client => ConfigureServicesHttpClient
            (
                  client
                , runtime.Services
            )
        );

        services.AddSingleton<IExternalAccountingGateway>
        (
            serviceProvider => serviceProvider.GetRequiredService<ServicesIntegrationGateway>()
        );
        services.AddSingleton<IWorkforceReferenceSource>
        (
            serviceProvider => serviceProvider.GetRequiredService<ServicesIntegrationGateway>()
        );
        services.AddSingleton<ILunchItemBootstrapSource>
        (
            serviceProvider => serviceProvider.GetRequiredService<ServicesIntegrationGateway>()
        );

        services.AddSingleton<IWorkforceReferenceCache, EfFfpReferenceCache>();
        services.AddSingleton<ReferenceDataRefreshService>();
        services.AddSingleton<IExternalLedgerOutbox, EfFfpLedgerOutbox>();
        services.AddSingleton<IExternalLedgerProjectionService, ExternalLedgerProjectionService>();
        services.AddHostedService<FfpReferenceRefreshWorker>();
        services.AddHostedService<FfpLedgerPublisherWorker>();
    }

    private static void ConfigureCafeteriaServicesIntegration
    (
          IServiceCollection services
        , CafeteriaServicesOptions options
    )
    {
        services.AddHttpClient<CafeteriaServicesClient>
        (
            client => ConfigureServicesHttpClient
            (
                  client
                , options
            )
        );

        services.AddHttpClient<CafeteriaManagementClient>
        (
            client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            }
        )
        .ConfigurePrimaryHttpMessageHandler
        (
            () => new HttpClientHandler
            {
                UseDefaultCredentials = true
            }
        );

        services.AddSingleton<ManagementEventWakeSignal>();
        services.AddHostedService<ManagementEventSyncWorker>();

        if (options.RealtimeEnabled)
        {
            services.AddHostedService<ManagementEventSignalRWorker>();
        }
    }

    private static void ConfigureServicesHttpClient
    (
          HttpClient client
        , CafeteriaServicesOptions options
    )
    {
        client.BaseAddress = new Uri
        (
            options.BaseUrl.TrimEnd('/') + "/"
        );

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Add
            (
                  "X-Cafeteria-Api-Key"
                , options.ApiKey
            );
        }
    }
}
