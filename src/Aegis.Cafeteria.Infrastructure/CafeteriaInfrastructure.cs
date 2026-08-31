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
 public static IServiceCollection AddCafeteriaPos(this IServiceCollection services,CafeteriaRuntimeOptions runtime)
 {
  services.AddSingleton(runtime); services.AddSingleton(runtime.Persistence); services.AddSingleton(runtime.FfpIntegration); services.AddSingleton(runtime.Health); services.AddSingleton(runtime.Messaging); services.AddSingleton(runtime.Services);
  services.AddSingleton(new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(runtime.Persistence)));
  services.AddSingleton<IDbContextFactory<LocalCafeteriaDbContext>>(sp=>sp.GetRequiredService<LocalDbContextFactory>());
  services.AddSingleton<LocalPersistenceStore>(); services.AddSingleton<EfCafeteriaPersistenceStore>();
  services.AddSingleton<IMonthlyCreditAllocationStore>(sp=>sp.GetRequiredService<EfCafeteriaPersistenceStore>());
  services.AddSingleton<ICafeteriaPersistenceStore>(sp=>new Aegis.Cafeteria.Infrastructure.Logging.LoggingCafeteriaPersistenceStore(sp.GetRequiredService<EfCafeteriaPersistenceStore>(),sp.GetRequiredService<ILogger<Aegis.Cafeteria.Infrastructure.Logging.LoggingCafeteriaPersistenceStore>>()));
  services.AddSingleton<IHolidayProvider,EfHolidayProvider>(); services.AddSingleton<IWorkingDayProvider,StandardWorkingDayProvider>();
  services.AddSingleton<ICafeteriaRatePolicyProvider,EfCafeteriaRatePolicyProvider>(); services.AddSingleton<IFfpPublicationStatusProvider,EfFfpPublicationStatusProvider>();
  services.AddSingleton<ICustomerAccountQuery,EfCustomerAccountQuery>(); services.AddSingleton<ICustomerIdentificationService,EfCustomerIdentificationService>(); services.AddSingleton<IProductCatalogQuery,EfProductCatalogQuery>(); services.AddSingleton<IOrderQuery,EfOrderQuery>(); services.AddSingleton<IRecentTransactionQuery,EfRecentTransactionQuery>();
  services.AddSingleton<ICustomerReferenceCacheStatus,EfCustomerReferenceCacheStatus>();
  if(runtime.FfpIntegration.Enabled){
   if(runtime.Services.Enabled)
   {
    services.AddHttpClient<ServicesIntegrationGateway>(c=>{c.BaseAddress=new Uri(runtime.Services.BaseUrl.TrimEnd('/')+'/'); if(!string.IsNullOrWhiteSpace(runtime.Services.ApiKey)) c.DefaultRequestHeaders.Add("X-Cafeteria-Api-Key",runtime.Services.ApiKey);});
    services.AddSingleton<IExternalAccountingGateway>(sp=>sp.GetRequiredService<ServicesIntegrationGateway>());
    services.AddSingleton<IWorkforceReferenceSource>(sp=>sp.GetRequiredService<ServicesIntegrationGateway>());
    services.AddSingleton<IProductCatalogSource>(sp=>sp.GetRequiredService<ServicesIntegrationGateway>());
   }
   else
   {
    services.AddSingleton<FfpSqlApplicationGateway>(); services.AddSingleton<IExternalAccountingGateway>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>()); services.AddSingleton<IWorkforceReferenceSource>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>()); services.AddSingleton<IProductCatalogSource>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>());
   }
   services.AddSingleton<IProductCatalogCache,EfProductCatalogCache>(); services.AddSingleton<ProductCatalogRefreshService>(); services.AddSingleton<IWorkforceReferenceCache,EfFfpReferenceCache>(); services.AddSingleton<ReferenceDataRefreshService>(); services.AddSingleton<IExternalLedgerOutbox,EfFfpLedgerOutbox>(); services.AddSingleton<IExternalLedgerProjectionService,ExternalLedgerProjectionService>(); services.AddHostedService<FfpReferenceRefreshWorker>(); services.AddHostedService<FfpLedgerPublisherWorker>();
  }
  services.AddSingleton<MonthlyAllocationCalculator>(); services.AddSingleton<IMonthlyAllocationExecutionPolicy,MonthlyAllocationExecutionPolicy>(); services.AddSingleton<MonthlyCreditAllocationService>(); services.AddSingleton<PosCheckoutService>();
  services.AddSingleton<EfWeeklyTransactionSummaryQuery>(); services.AddSingleton<IWeeklyTransactionSummaryQuery>(sp=>sp.GetRequiredService<EfWeeklyTransactionSummaryQuery>()); services.AddSingleton<IWeeklySummaryDispatchStore>(sp=>sp.GetRequiredService<EfWeeklyTransactionSummaryQuery>()); services.AddSingleton<WeeklyCustomerSummaryService>();
  if(runtime.Messaging.Enabled){ services.AddHttpClient<ICafeteriaMessagingGateway,HttpCafeteriaMessagingGateway>(); } else services.AddSingleton<ICafeteriaMessagingGateway,DisabledCafeteriaMessagingGateway>();
  if(runtime.Services.Enabled){ services.AddHttpClient<CafeteriaServicesClient>(c=>{c.BaseAddress=new Uri(runtime.Services.BaseUrl.TrimEnd('/')+'/'); if(!string.IsNullOrWhiteSpace(runtime.Services.ApiKey)) c.DefaultRequestHeaders.Add("X-Cafeteria-Api-Key",runtime.Services.ApiKey);}); services.AddHostedService<ManagementEventSyncWorker>(); }
  services.AddSingleton<PersistenceInitializer>(); services.AddHostedService<CafeteriaHeartbeatWorker>(); return services;
 }
}
